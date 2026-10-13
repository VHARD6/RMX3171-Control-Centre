package com.rmx.cachecleaner;

import android.os.Environment;
import android.os.StatFs;
import android.util.Log;

import androidx.annotation.NonNull;

import java.util.Locale;

/**
 * Storage measurement utility for RMX Device Hub.
 *
 * Core Guarantees & Transparency:
 * 1. Reads device internal storage available bytes using StatFs on /data.
 * 2. Compares before and after measurements honestly:
 *    - Positive difference: Reclaimed storage observed.
 *    - Zero difference: No storage change observed.
 *    - Negative difference: Storage decreased due to concurrent background system activity.
 *    - Invalid / unavailable: Explicitly marked as unavailable.
 * 3. Never claims that observed storage change equals exact cache deleted,
 *    acknowledging concurrent background writes and OS quota allocations.
 */
public final class StorageMeasurementUtil {

    private static final String TAG = "RMX_StorageMeasure";

    public enum DeltaType {
        POSITIVE_RECLAIMED,
        ZERO_UNCHANGED,
        NEGATIVE_FLUCTUATION,
        UNAVAILABLE
    }

    public interface StorageProvider {
        long getAvailableBytes();
    }

    private static final StorageProvider DEFAULT_PROVIDER = new StorageProvider() {
        @Override
        public long getAvailableBytes() {
            try {
                StatFs stat = new StatFs(Environment.getDataDirectory().getAbsolutePath());
                return stat.getAvailableBytes();
            } catch (Throwable t) {
                Log.e(TAG, "Failed to inspect StatFs available bytes", t);
                return -1;
            }
        }
    };

    private static StorageProvider sStorageProvider = DEFAULT_PROVIDER;

    private StorageMeasurementUtil() {}

    /**
     * For unit test injection.
     */
    public static void setStorageProviderForTesting(StorageProvider provider) {
        sStorageProvider = (provider != null) ? provider : DEFAULT_PROVIDER;
    }

    public static void resetStorageProviderForTesting() {
        sStorageProvider = DEFAULT_PROVIDER;
    }

    /**
     * Reads current available storage in bytes on /data. Returns -1 if unavailable.
     */
    public static long getAvailableStorageBytes() {
        try {
            return sStorageProvider.getAvailableBytes();
        } catch (Throwable t) {
            Log.e(TAG, "Error obtaining available storage bytes", t);
            return -1;
        }
    }

    /**
     * Encapsulates a before-and-after storage measurement comparison.
     */
    public static class StorageMeasurement {
        public final long bytesBefore;
        public final long bytesAfter;
        public final long deltaBytes;
        public final DeltaType deltaType;
        public final boolean isValid;

        public StorageMeasurement(long bytesBefore, long bytesAfter) {
            this.bytesBefore = bytesBefore;
            this.bytesAfter = bytesAfter;

            if (bytesBefore <= 0 || bytesAfter <= 0) {
                this.deltaBytes = 0;
                this.deltaType = DeltaType.UNAVAILABLE;
                this.isValid = false;
            } else {
                this.deltaBytes = bytesAfter - bytesBefore;
                this.isValid = true;
                if (this.deltaBytes > 0) {
                    this.deltaType = DeltaType.POSITIVE_RECLAIMED;
                } else if (this.deltaBytes == 0) {
                    this.deltaType = DeltaType.ZERO_UNCHANGED;
                } else {
                    this.deltaType = DeltaType.NEGATIVE_FLUCTUATION;
                }
            }
        }

        /**
         * Formatted delta string, e.g. "+24.50 MB", "0 B", "-3.20 MB", or "Unavailable".
         */
        @NonNull
        public String getFormattedDelta() {
            switch (deltaType) {
                case POSITIVE_RECLAIMED:
                    return "+" + formatBytes(deltaBytes);
                case ZERO_UNCHANGED:
                    return "0 B";
                case NEGATIVE_FLUCTUATION:
                    return "-" + formatBytes(Math.abs(deltaBytes));
                case UNAVAILABLE:
                default:
                    return "Unavailable";
            }
        }

        /**
         * Transparent user-facing explanation of the measured storage change.
         */
        @NonNull
        public String getDescription() {
            switch (deltaType) {
                case POSITIVE_RECLAIMED:
                    return String.format(Locale.US,
                            "Measured storage change: +%s (Free before: %s → Free after: %s).",
                            formatBytes(deltaBytes), formatBytes(bytesBefore), formatBytes(bytesAfter));
                case ZERO_UNCHANGED:
                    return String.format(Locale.US,
                            "No storage change observed (Free storage remained %s).",
                            formatBytes(bytesBefore));
                case NEGATIVE_FLUCTUATION:
                    return String.format(Locale.US,
                            "Storage decreased by %s due to concurrent background system activity (Free before: %s → Free after: %s).",
                            formatBytes(Math.abs(deltaBytes)), formatBytes(bytesBefore), formatBytes(bytesAfter));
                case UNAVAILABLE:
                default:
                    return "Storage measurement was unavailable or could not be verified.";
            }
        }
    }

    public static StorageMeasurement calculateMeasurement(long bytesBefore, long bytesAfter) {
        return new StorageMeasurement(bytesBefore, bytesAfter);
    }

    /**
     * Formats bytes into human-readable B, KB, MB, or GB.
     */
    @NonNull
    public static String formatBytes(long bytes) {
        long abs = Math.abs(bytes);
        if (abs < 1024) {
            return abs + " B";
        } else if (abs < 1024 * 1024) {
            return String.format(Locale.US, "%.2f KB", abs / 1024.0);
        } else if (abs < 1024 * 1024 * 1024) {
            return String.format(Locale.US, "%.2f MB", abs / (1024.0 * 1024.0));
        } else {
            return String.format(Locale.US, "%.2f GB", abs / (1024.0 * 1024.0 * 1024.0));
        }
    }
}
