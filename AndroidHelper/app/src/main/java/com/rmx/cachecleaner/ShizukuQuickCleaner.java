package com.rmx.cachecleaner;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Environment;
import android.os.IBinder;
import android.os.ParcelFileDescriptor;
import android.os.StatFs;
import android.util.Log;

import java.io.BufferedReader;
import java.io.FileInputStream;
import java.io.InputStreamReader;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

import moe.shizuku.server.IRemoteProcess;
import moe.shizuku.server.IShizukuService;
import rikka.shizuku.Shizuku;

/**
 * Phase 7: Shizuku-powered One-Tap Quick Clean All.
 * Executes Android's system-managed cache trimming command (`pm trim-caches 100G`)
 * using Shizuku's authorized shell Binder context (UID 2000).
 *
 * Scope & Transparency:
 * - Trims quota-managed Android system and download caches down to the system floor.
 * - Measures exact available storage before and after using StatFs.
 * - Distinguishes system quota cache reclamation from per-app private cache zeroing.
 * - Safely handles timeouts, command failure, and permission states.
 * - NEVER wipes application user data, databases, or accounts.
 */
public final class ShizukuQuickCleaner {

    private static final String TAG = "RMX_QuickCleaner";
    private static final String TRIM_SIZE = "100G";
    private static final long PROCESS_TIMEOUT_SECONDS = 25;

    public static class QuickCleanResult {
        public final boolean success;
        public final int exitCode;
        public final long freeBytesBefore;
        public final long freeBytesAfter;
        public final long bytesReclaimed;
        public final long durationMs;
        public final String stdout;
        public final String stderr;
        public final String summaryMessage;
        public final boolean isShizukuUnavailable;
        public final boolean isShizukuUnauthorized;

        public QuickCleanResult(boolean success, int exitCode, long freeBytesBefore, long freeBytesAfter,
                                long bytesReclaimed, long durationMs, String stdout, String stderr,
                                String summaryMessage, boolean isShizukuUnavailable, boolean isShizukuUnauthorized) {
            this.success = success;
            this.exitCode = exitCode;
            this.freeBytesBefore = freeBytesBefore;
            this.freeBytesAfter = freeBytesAfter;
            this.bytesReclaimed = bytesReclaimed;
            this.durationMs = durationMs;
            this.stdout = stdout != null ? stdout : "";
            this.stderr = stderr != null ? stderr : "";
            this.summaryMessage = summaryMessage;
            this.isShizukuUnavailable = isShizukuUnavailable;
            this.isShizukuUnauthorized = isShizukuUnauthorized;
        }

        public static QuickCleanResult error(String message, boolean unavailable, boolean unauthorized) {
            return new QuickCleanResult(false, -1, 0, 0, 0, 0, "", "", message, unavailable, unauthorized);
        }
    }

    public interface QuickCleanCallback {
        void onComplete(QuickCleanResult result);
    }

    private static final ExecutorService EXECUTOR = Executors.newSingleThreadExecutor();

    private ShizukuQuickCleaner() {}

    /**
     * Checks if Shizuku is currently installed, running, and granted permission.
     */
    public static boolean isReadyForQuickClean(Context context) {
        return ShizukuCacheCleaner.isShizukuInstalled(context)
                && ShizukuCacheCleaner.isShizukuRunning()
                && ShizukuCacheCleaner.isShizukuAuthorized();
    }

    /**
     * Measures available storage in bytes on /data using StatFs.
     */
    public static long getAvailableStorageBytes() {
        try {
            StatFs stat = new StatFs(Environment.getDataDirectory().getAbsolutePath());
            return stat.getAvailableBytes();
        } catch (Throwable t) {
            Log.e(TAG, "Failed to read StatFs available bytes", t);
            return 0;
        }
    }

    /**
     * Executes `pm trim-caches 100G` asynchronously off the main UI thread.
     */
    public static void executeQuickClean(final Context context, final QuickCleanCallback callback) {
        EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                QuickCleanResult result = runQuickCleanInternal(context);
                if (callback != null) {
                    callback.onComplete(result);
                }
            }
        });
    }

    /**
     * Internal synchronous execution logic.
     */
    public static QuickCleanResult runQuickCleanInternal(Context context) {
        if (!ShizukuCacheCleaner.isShizukuInstalled(context)) {
            return QuickCleanResult.error(
                    "Shizuku companion application is not installed.", true, false);
        }

        if (!ShizukuCacheCleaner.isShizukuRunning()) {
            return QuickCleanResult.error(
                    "Shizuku service is not running. Please start Shizuku and try again.", true, false);
        }

        if (!ShizukuCacheCleaner.isShizukuAuthorized()) {
            return QuickCleanResult.error(
                    "RMX Cache Cleaner is not authorized in Shizuku. Please grant authorization.", false, true);
        }

        long freeBefore = getAvailableStorageBytes();
        long startTime = System.currentTimeMillis();

        IRemoteProcess remoteProcess = null;
        int exitCode = -1;
        String stdout = "";
        String stderr = "";

        try {
            IBinder binder = Shizuku.getBinder();
            if (binder == null) {
                return QuickCleanResult.error("Shizuku binder is null; connection lost.", true, false);
            }

            IShizukuService service = IShizukuService.Stub.asInterface(binder);
            if (service == null) {
                return QuickCleanResult.error("Unable to bind to IShizukuService.", true, false);
            }

            Log.i(TAG, "Executing 'pm trim-caches " + TRIM_SIZE + "' via Shizuku shell context...");
            String[] cmd = new String[]{"pm", "trim-caches", TRIM_SIZE};
            remoteProcess = service.newProcess(cmd, null, null);

            if (remoteProcess == null) {
                return QuickCleanResult.error("Failed to spawn process through Shizuku service.", false, false);
            }

            // Await completion with timeout
            boolean finishedInTime = remoteProcess.waitForTimeout(PROCESS_TIMEOUT_SECONDS, TimeUnit.SECONDS.name());
            if (!finishedInTime) {
                Log.w(TAG, "Command timed out after " + PROCESS_TIMEOUT_SECONDS + "s; terminating process.");
                try {
                    remoteProcess.destroy();
                } catch (Throwable ignored) {}
                return QuickCleanResult.error("Cache trim operation timed out after " + PROCESS_TIMEOUT_SECONDS + " seconds.", false, false);
            }

            exitCode = remoteProcess.exitValue();
            stdout = readStream(remoteProcess.getInputStream());
            stderr = readStream(remoteProcess.getErrorStream());

            Log.i(TAG, "pm trim-caches completed with exitCode=" + exitCode
                    + ", stdout='" + stdout + "', stderr='" + stderr + "'");

        } catch (Throwable t) {
            Log.e(TAG, "Exception while executing Shizuku quick clean", t);
            return QuickCleanResult.error("Command execution failed: " + t.getMessage(), false, false);
        } finally {
            if (remoteProcess != null) {
                try {
                    remoteProcess.destroy();
                } catch (Throwable ignored) {}
            }
        }

        long durationMs = System.currentTimeMillis() - startTime;
        long freeAfter = getAvailableStorageBytes();
        long delta = freeAfter - freeBefore;
        long reclaimed = Math.max(0, delta);

        boolean success = (exitCode == 0);
        String summary;
        if (!success) {
            summary = String.format(Locale.US, "Command returned exit code %d: %s", exitCode,
                    stderr.isEmpty() ? (stdout.isEmpty() ? "Unknown error" : stdout) : stderr);
        } else if (reclaimed > 0) {
            summary = String.format(Locale.US,
                    "System cache trimmed successfully! Reclaimed %s of system and quota-managed cache.",
                    formatBytes(reclaimed));
        } else {
            summary = "System cache is already optimal. No purgeable quota cache was released by Android installd.";
        }

        return new QuickCleanResult(success, exitCode, freeBefore, freeAfter, reclaimed,
                durationMs, stdout, stderr, summary, false, false);
    }

    private static String readStream(ParcelFileDescriptor pfd) {
        if (pfd == null) return "";
        StringBuilder sb = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(
                new InputStreamReader(new FileInputStream(pfd.getFileDescriptor())))) {
            String line;
            while ((line = reader.readLine()) != null) {
                if (sb.length() > 0) sb.append("\n");
                sb.append(line);
            }
        } catch (Throwable ignored) {}
        return sb.toString().trim();
    }

    public static String formatBytes(long bytes) {
        if (bytes < 1024) {
            return bytes + " B";
        } else if (bytes < 1024 * 1024) {
            return String.format(Locale.US, "%.2f KB", bytes / 1024.0);
        } else if (bytes < 1024 * 1024 * 1024) {
            return String.format(Locale.US, "%.2f MB", bytes / (1024.0 * 1024.0));
        } else {
            return String.format(Locale.US, "%.2f GB", bytes / (1024.0 * 1024.0 * 1024.0));
        }
    }
}
