package com.rmx.cachecleaner;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Binder;
import android.os.IBinder;
import android.os.Parcel;
import android.os.RemoteException;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

import rikka.shizuku.Shizuku;
import rikka.shizuku.ShizukuBinderWrapper;
import rikka.shizuku.SystemServiceHelper;

public final class ShizukuCacheCleaner {

    public static final String SHIZUKU_PACKAGE = "moe.shizuku.privileged.api";
    public static final String SHIZUKU_WEBSITE = "https://shizuku.rikka.app";

    public static final int SCOPE_USER_APPS = 0;
    public static final int SCOPE_USER_AND_SYSTEM = 1;

    public static final int RESULT_SUCCESS = 1;
    public static final int RESULT_FAILED = 0;
    public static final int RESULT_TIMED_OUT = -1;

    private static final int TIMEOUT_PER_PACKAGE_MS = 1000;

    private final Context context;
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean isCancelled = new AtomicBoolean(false);

    public static class CleanResult {
        public final int totalPackages;
        public final int processed;
        public final int successful;
        public final int failed;
        public final int skipped;
        public final int scope;
        public final boolean wasCancelled;
        public final List<String> excludedPackages;
        public final List<String> failedPackages;
        public final boolean isPermissionUnsupported;
        public final String limitationReason;

        public CleanResult(int totalPackages, int processed, int successful, int failed, int skipped,
                           int scope, boolean wasCancelled, List<String> excludedPackages, List<String> failedPackages,
                           boolean isPermissionUnsupported, String limitationReason) {
            this.totalPackages = totalPackages;
            this.processed = processed;
            this.successful = successful;
            this.failed = failed;
            this.skipped = skipped;
            this.scope = scope;
            this.wasCancelled = wasCancelled;
            this.excludedPackages = Collections.unmodifiableList(excludedPackages);
            this.failedPackages = Collections.unmodifiableList(failedPackages);
            this.isPermissionUnsupported = isPermissionUnsupported;
            this.limitationReason = limitationReason;
        }

        public CleanResult(int totalPackages, int processed, int successful, int failed, int skipped,
                           int scope, boolean wasCancelled, List<String> excludedPackages, List<String> failedPackages) {
            this(totalPackages, processed, successful, failed, skipped, scope, wasCancelled, excludedPackages, failedPackages, false, null);
        }
    }

    public interface ProgressListener {
        void onProgress(int current, int total, String packageName, String label, int success, int failed, int skipped);
    }

    public interface CompletionListener {
        void onComplete(CleanResult result);
    }

    public ShizukuCacheCleaner(Context context) {
        this.context = context.getApplicationContext();
    }

    public static boolean isShizukuInstalled(Context context) {
        try {
            context.getPackageManager().getPackageInfo(SHIZUKU_PACKAGE, 0);
            return true;
        } catch (PackageManager.NameNotFoundException e) {
            return false;
        }
    }

    public static boolean isShizukuRunning() {
        try {
            return Shizuku.pingBinder();
        } catch (Throwable t) {
            return false;
        }
    }

    public static boolean isShizukuAuthorized() {
        try {
            return isShizukuRunning() && Shizuku.checkSelfPermission() == PackageManager.PERMISSION_GRANTED;
        } catch (Throwable t) {
            return false;
        }
    }

    public static boolean isInternalDeleteCachePermissionSupported(Context context) {
        try {
            return context.getPackageManager().checkPermission(
                "android.permission.INTERNAL_DELETE_CACHE_FILES", "com.android.shell"
            ) == PackageManager.PERMISSION_GRANTED;
        } catch (Throwable t) {
            return false;
        }
    }

    public static void requestShizukuPermission(int requestCode) {
        try {
            if (isShizukuRunning() && Shizuku.checkSelfPermission() != PackageManager.PERMISSION_GRANTED) {
                Shizuku.requestPermission(requestCode);
            }
        } catch (Throwable ignored) {}
    }

    public static void openShizukuApp(Context context) {
        try {
            Intent intent = context.getPackageManager().getLaunchIntentForPackage(SHIZUKU_PACKAGE);
            if (intent != null) {
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                context.startActivity(intent);
                return;
            }
        } catch (Throwable ignored) {}
        openShizukuWebsite(context);
    }

    public static void openShizukuWebsite(Context context) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(SHIZUKU_WEBSITE));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
        } catch (Throwable ignored) {}
    }

    public void cancel() {
        isCancelled.set(true);
    }

    public boolean isCancelled() {
        return isCancelled.get();
    }

    public void startClean(int scope, ProgressListener progressListener, CompletionListener completionListener) {
        isCancelled.set(false);
        executor.execute(() -> {
            CleanResult result = executeClean(scope, progressListener);
            if (completionListener != null) {
                completionListener.onComplete(result);
            }
        });
    }

    private CleanResult executeClean(int scope, ProgressListener progressListener) {
        PackageManager pm = context.getPackageManager();
        List<PackageInfo> installedPackages = pm.getInstalledPackages(0);
        if (installedPackages == null) {
            installedPackages = Collections.emptyList();
        }

        if (!isInternalDeleteCachePermissionSupported(context)) {
            android.util.Log.w("ShizukuCleaner", "Calling UID 2000 does not have android.permission.INTERNAL_DELETE_CACHE_FILES. Aborting deep clean to avoid silent timeouts.");
            return new CleanResult(installedPackages.size(), 0, 0, 0, 0, scope, false,
                    Collections.emptyList(), Collections.emptyList(), true,
                    "Calling UID 2000 lacks android.permission.INTERNAL_DELETE_CACHE_FILES; ignored by system_server.");
        }

        Object ipm = null;
        try {
            ipm = getIPackageManager();
        } catch (Throwable t) {
            // Cannot bind to PackageManager through Shizuku
            return new CleanResult(0, 0, 0, 0, 0, scope, false, Collections.emptyList(), Collections.emptyList());
        }

        int total = installedPackages.size();
        int processed = 0;
        int successful = 0;
        int failed = 0;
        int skipped = 0;

        List<String> excludedList = new ArrayList<>();
        List<String> failedList = new ArrayList<>();

        for (int i = 0; i < total; i++) {
            if (isCancelled.get()) {
                break;
            }

            PackageInfo pi = installedPackages.get(i);
            String packageName = pi.packageName;
            ApplicationInfo ai = pi.applicationInfo;
            String appLabel = (ai != null) ? pm.getApplicationLabel(ai).toString() : packageName;

            // Check exclusion list
            if (ProtectedPackages.isExcluded(packageName)) {
                excludedList.add(packageName);
                skipped++;
                if (progressListener != null) {
                    progressListener.onProgress(i + 1, total, packageName, appLabel, successful, failed, skipped);
                }
                continue;
            }

            // Check enabled state
            if (ai != null && !ai.enabled) {
                skipped++;
                if (progressListener != null) {
                    progressListener.onProgress(i + 1, total, packageName, appLabel, successful, failed, skipped);
                }
                continue;
            }

            // Check scope filter
            boolean isSystem = false;
            if (ai != null) {
                isSystem = (ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0 ||
                           (ai.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) != 0;
            }

            if (scope == SCOPE_USER_APPS && isSystem) {
                skipped++;
                if (progressListener != null) {
                    progressListener.onProgress(i + 1, total, packageName, appLabel, successful, failed, skipped);
                }
                continue;
            }

            // Eligible package: attempt deletion via Shizuku Binder context
            processed++;
            int status = deletePackageCache(ipm, packageName);
            if (status == RESULT_SUCCESS) {
                successful++;
            } else if (status == RESULT_TIMED_OUT && successful == 0) {
                failed++;
                failedList.add(packageName);
                android.util.Log.w("ShizukuCleaner", "Package cache deletion timed out without callback; system_server is silently ignoring calls.");
                return new CleanResult(total, processed, successful, failed, skipped, scope, false,
                        excludedList, failedList, true,
                        "PackageManager calls silently ignored by system_server (missing INTERNAL_DELETE_CACHE_FILES).");
            } else {
                failed++;
                failedList.add(packageName);
            }

            if (progressListener != null) {
                progressListener.onProgress(i + 1, total, packageName, appLabel, successful, failed, skipped);
            }
        }

        return new CleanResult(total, processed, successful, failed, skipped, scope, isCancelled.get(), excludedList, failedList);
    }

    private static Object getIPackageManager() throws Exception {
        IBinder rawBinder = SystemServiceHelper.getSystemService("package");
        if (rawBinder == null) {
            android.util.Log.e("ShizukuCleaner", "SystemServiceHelper.getSystemService('package') returned null");
            throw new IllegalStateException("Failed to obtain 'package' system service binder");
        }
        IBinder wrapped = new ShizukuBinderWrapper(rawBinder);
        Class<?> stubClass = Class.forName("android.content.pm.IPackageManager$Stub");
        Method asInterface = stubClass.getMethod("asInterface", IBinder.class);
        Object ipm = asInterface.invoke(null, wrapped);
        android.util.Log.i("ShizukuCleaner", "Successfully obtained IPackageManager: " + ipm);
        return ipm;
    }

    private static int deletePackageCache(Object ipm, String packageName) {
        if (ipm == null || packageName == null) {
            android.util.Log.e("ShizukuCleaner", "ipm or packageName is null");
            return RESULT_FAILED;
        }

        final CountDownLatch latch = new CountDownLatch(1);
        final boolean[] successHolder = new boolean[]{false};

        try {
            Class<?> observerClass = Class.forName("android.content.pm.IPackageDataObserver");

            final Binder observerBinder = new Binder() {
                @Override
                protected boolean onTransact(int code, Parcel data, Parcel reply, int flags) throws RemoteException {
                    android.util.Log.i("ShizukuCleaner", "observer onTransact code=" + code);
                    if (code == FIRST_CALL_TRANSACTION) {
                        try {
                            data.enforceInterface("android.content.pm.IPackageDataObserver");
                            String pkg = data.readString(); // package name
                            int succ = data.readInt();
                            successHolder[0] = (succ != 0);
                            android.util.Log.i("ShizukuCleaner", "onTransact FIRST_CALL: pkg=" + pkg + " succ=" + succ);
                        } catch (Throwable t) {
                            android.util.Log.e("ShizukuCleaner", "onTransact parse error", t);
                        }
                        latch.countDown();
                        return true;
                    } else if (code == INTERFACE_TRANSACTION) {
                        reply.writeString("android.content.pm.IPackageDataObserver");
                        return true;
                    }
                    return super.onTransact(code, data, reply, flags);
                }
            };

            Object observerProxy = Proxy.newProxyInstance(
                observerClass.getClassLoader(),
                new Class<?>[]{ observerClass },
                new InvocationHandler() {
                    @Override
                    public Object invoke(Object proxy, Method method, Object[] args) throws Throwable {
                        android.util.Log.i("ShizukuCleaner", "observerProxy invoke: " + method.getName());
                        if ("asBinder".equals(method.getName())) {
                            return observerBinder;
                        }
                        if ("onRemoveCompleted".equals(method.getName())) {
                            if (args != null && args.length >= 2 && args[1] instanceof Boolean) {
                                successHolder[0] = (Boolean) args[1];
                            }
                            android.util.Log.i("ShizukuCleaner", "observerProxy onRemoveCompleted: " + successHolder[0]);
                            latch.countDown();
                            return null;
                        }
                        return null;
                    }
                }
            );

            Method targetMethod = null;
            boolean isUserMethod = false;

            for (Method m : ipm.getClass().getMethods()) {
                if ("deleteApplicationCacheFilesAsUser".equals(m.getName()) && m.getParameterTypes().length == 3) {
                    targetMethod = m;
                    isUserMethod = true;
                    break;
                }
            }

            if (targetMethod == null) {
                for (Method m : ipm.getClass().getMethods()) {
                    if ("deleteApplicationCacheFiles".equals(m.getName()) && m.getParameterTypes().length == 2) {
                        targetMethod = m;
                        isUserMethod = false;
                        break;
                    }
                }
            }

            if (targetMethod == null) {
                android.util.Log.e("ShizukuCleaner", "targetMethod NOT FOUND in " + ipm.getClass());
                return RESULT_FAILED;
            }

            android.util.Log.i("ShizukuCleaner", "Invoking " + targetMethod.getName() + " for " + packageName);
            if (isUserMethod) {
                targetMethod.invoke(ipm, packageName, 0, observerProxy);
            } else {
                targetMethod.invoke(ipm, packageName, observerProxy);
            }
            android.util.Log.i("ShizukuCleaner", "Invoked targetMethod successfully, awaiting latch...");

            boolean received = latch.await(TIMEOUT_PER_PACKAGE_MS, TimeUnit.MILLISECONDS);
            android.util.Log.i("ShizukuCleaner", "Latch completed: received=" + received + ", success=" + successHolder[0]);
            if (!received) {
                return RESULT_TIMED_OUT;
            }
            return successHolder[0] ? RESULT_SUCCESS : RESULT_FAILED;
        } catch (Throwable t) {
            android.util.Log.e("ShizukuCleaner", "Exception in deletePackageCache for " + packageName, t);
            return RESULT_FAILED;
        }
    }
}
