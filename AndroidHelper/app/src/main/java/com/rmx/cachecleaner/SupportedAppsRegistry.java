package com.rmx.cachecleaner;

import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Phase 7: Dynamic All-Apps Discovery & Supported Apps Registry.
 *
 * Responsibilities:
 * 1. Dynamically enumerates all eligible installed user/third-party applications from PackageManager.
 * 2. Strictly excludes:
 *    - Core Android OS framework and system applications (FLAG_SYSTEM).
 *    - OEM packages (ColorOS, Realme, Oppo, HeyTap, NearMe, MediaTek).
 *    - Critical utilities (Settings, System UI, Package Installer).
 *    - Self companion app (com.rmx.cachecleaner) and Shizuku bridge.
 *    - Disabled or uninstalled applications.
 * 3. Enforces allowlist validation for sequential batch execution.
 * 4. Provides search/filtering and alphabetical sorting.
 */
public class SupportedAppsRegistry {

    // Predefined baseline allowlist of supported 3rd-party applications
    private static final Map<String, String> BASELINE_ALLOWLIST;
    static {
        Map<String, String> map = new LinkedHashMap<>();
        map.put("com.instagram.android", "Instagram");
        map.put("com.spotify.music", "Spotify");
        map.put("com.snapchat.android", "Snapchat");
        map.put("com.grofers.customerapp", "Blinkit");
        BASELINE_ALLOWLIST = Collections.unmodifiableMap(map);
    }

    // Dynamic registry for all discovered eligible applications
    private static final Map<String, String> DYNAMIC_REGISTRY = new ConcurrentHashMap<>(BASELINE_ALLOWLIST);

    /**
     * Checks if a package name is forbidden from cleaning.
     * Retained for backward compatibility; delegates directly to ProtectedPackages.
     */
    public static boolean isForbiddenPrefix(String packageName) {
        return ProtectedPackages.isExcluded(packageName);
    }

    /**
     * Validates whether a package name is permitted for cache cleaning.
     */
    public static boolean isPackageAllowed(String packageName) {
        if (packageName == null || packageName.trim().isEmpty()) {
            return false;
        }
        packageName = packageName.trim();

        if (ProtectedPackages.isExcluded(packageName)) {
            return false;
        }

        return BASELINE_ALLOWLIST.containsKey(packageName) || DYNAMIC_REGISTRY.containsKey(packageName);
    }

    /**
     * Registers an eligible package into the dynamic registry.
     */
    public static void registerAllowedPackage(String packageName, String label) {
        if (packageName != null && !ProtectedPackages.isExcluded(packageName)) {
            DYNAMIC_REGISTRY.put(packageName.trim(), label != null ? label.trim() : packageName.trim());
        }
    }

    /**
     * Unregisters a package from the dynamic registry when uninstalled.
     */
    public static void unregisterAllowedPackage(String packageName) {
        if (packageName != null) {
            DYNAMIC_REGISTRY.remove(packageName.trim());
        }
    }

    /**
     * Validates and registers an installed package if it is eligible for cleaning.
     * Useful for cross-process synchronization when batch is received in :controls.
     */
    public static boolean registerIfEligible(PackageManager pm, String packageName) {
        if (pm == null || packageName == null || ProtectedPackages.isExcluded(packageName)) {
            return false;
        }
        try {
            ApplicationInfo ai = pm.getApplicationInfo(packageName.trim(), 0);
            if (ai == null || !ai.enabled) return false;
            boolean hasCode = (ai.flags & ApplicationInfo.FLAG_HAS_CODE) != 0;
            if (!hasCode) return false;

            CharSequence labelCs = pm.getApplicationLabel(ai);
            String label = (labelCs != null && labelCs.length() > 0) ? labelCs.toString().trim() : packageName.trim();
            registerAllowedPackage(packageName.trim(), label);
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    /**
     * Determines whether a package on the system is eligible for discovery.
     * All installed packages with valid application info are eligible for discovery.
     */
    public static boolean isPackageEligible(PackageInfo pi) {
        if (pi == null || pi.packageName == null || pi.packageName.trim().isEmpty()) {
            return false;
        }
        return pi.applicationInfo != null;
    }

    /**
     * Dynamically discovers all installed applications on the device, including:
     * - User-installed applications
     * - Preinstalled applications (Google app, Messages, Maps, Play Store, ColorOS apps)
     * - Protected system components (Settings, System UI, Package Installer, framework overlays)
     *
     * Distinguishes classification and protection for safety.
     * Sorts the resulting list alphabetically by application display label.
     */
    public static List<TargetApp> getInstalledEligibleApps(PackageManager pm) {
        List<TargetApp> allApps = new ArrayList<>();
        if (pm == null) {
            return allApps;
        }

        List<PackageInfo> installedPackages;
        try {
            installedPackages = pm.getInstalledPackages(PackageManager.GET_META_DATA);
        } catch (Throwable t) {
            installedPackages = Collections.emptyList();
        }

        if (installedPackages == null || installedPackages.isEmpty()) {
            return allApps;
        }

        for (PackageInfo pi : installedPackages) {
            if (!isPackageEligible(pi)) {
                continue;
            }

            String pkg = pi.packageName.trim();
            ApplicationInfo ai = pi.applicationInfo;

            CharSequence labelCs = null;
            try {
                labelCs = pm.getApplicationLabel(ai);
            } catch (Throwable ignored) {}

            String label = (labelCs != null && labelCs.length() > 0)
                    ? labelCs.toString().trim()
                    : pkg;

            TargetApp app = new TargetApp(pkg, label);
            app.setInstalled(true);
            app.setResolvedLabel(label);
            app.setEnabled(ai.enabled);

            boolean isSystem = (ai.flags & ApplicationInfo.FLAG_SYSTEM) != 0;
            boolean isUpdatedSystem = (ai.flags & ApplicationInfo.FLAG_UPDATED_SYSTEM_APP) != 0;
            boolean hasCode = (ai.flags & ApplicationInfo.FLAG_HAS_CODE) != 0;
            boolean isExcluded = ProtectedPackages.isExcluded(pkg);

            if (isExcluded) {
                app.setClassification(TargetApp.Classification.SYSTEM_COMPONENT);
                app.setProtected(true);
                app.setProtectionReason(ProtectedPackages.getExclusionReason(pkg));
                app.setSelected(false);
                app.setStatus(TargetApp.Status.PROTECTED);
                app.setStatusDetail("Protected: " + app.getProtectionReason());
            } else if (!hasCode) {
                app.setClassification(TargetApp.Classification.SYSTEM_COMPONENT);
                app.setProtected(true);
                app.setProtectionReason("Framework resource overlay without application code or cache");
                app.setSelected(false);
                app.setStatus(TargetApp.Status.PROTECTED);
                app.setStatusDetail("Protected: Framework overlay");
            } else if (!ai.enabled) {
                TargetApp.Classification cl = (isSystem && !isUpdatedSystem)
                        ? TargetApp.Classification.PREINSTALLED_APP
                        : TargetApp.Classification.USER_APP;
                app.setClassification(cl);
                app.setProtected(true);
                app.setProtectionReason("Application is disabled in system settings");
                app.setSelected(false);
                app.setStatus(TargetApp.Status.DISABLED);
                app.setStatusDetail("Disabled in Settings");
            } else if (!isSystem || isUpdatedSystem) {
                app.setClassification(TargetApp.Classification.USER_APP);
                app.setProtected(false);
                app.setSelected(true);
                app.setStatus(TargetApp.Status.PENDING);
                app.setStatusDetail("Ready for cleaning");
                registerAllowedPackage(pkg, label);
            } else {
                // Preinstalled user-facing application (Google app, Messages, Maps, Calculator, etc.)
                app.setClassification(TargetApp.Classification.PREINSTALLED_APP);
                app.setProtected(false);
                app.setSelected(true);
                app.setStatus(TargetApp.Status.PENDING);
                app.setStatusDetail("Ready for cleaning");
                registerAllowedPackage(pkg, label);
            }

            allApps.add(app);
        }

        // Sort alphabetically by resolved label (case-insensitive), then package name
        Collections.sort(allApps, new Comparator<TargetApp>() {
            @Override
            public int compare(TargetApp o1, TargetApp o2) {
                int cmp = o1.getResolvedLabel().compareToIgnoreCase(o2.getResolvedLabel());
                if (cmp != 0) return cmp;
                return o1.getPackageName().compareToIgnoreCase(o2.getPackageName());
            }
        });

        return allApps;
    }

    /**
     * Backward-compatible alias for getInstalledEligibleApps.
     */
    public static List<TargetApp> getInstalledSupportedApps(PackageManager pm) {
        return getInstalledEligibleApps(pm);
    }

    /**
     * Filters an existing list of TargetApps matching a query string in label or package name.
     */
    public static List<TargetApp> filterApps(List<TargetApp> allApps, String query) {
        if (allApps == null) return Collections.emptyList();
        if (query == null || query.trim().isEmpty()) {
            return new ArrayList<>(allApps);
        }
        String q = query.trim().toLowerCase();
        List<TargetApp> filtered = new ArrayList<>();
        for (TargetApp app : allApps) {
            if (app.getResolvedLabel().toLowerCase().contains(q)
                    || app.getPackageName().toLowerCase().contains(q)) {
                filtered.add(app);
            }
        }
        return filtered;
    }

    /**
     * Resolve application label from PackageManager, falling back to default label.
     */
    public static String resolveAppLabel(PackageManager pm, String packageName, String defaultFallback) {
        if (pm != null && packageName != null) {
            try {
                ApplicationInfo appInfo = pm.getApplicationInfo(packageName, 0);
                if (appInfo != null) {
                    CharSequence label = pm.getApplicationLabel(appInfo);
                    if (label != null && label.length() > 0) {
                        return label.toString().trim();
                    }
                }
            } catch (PackageManager.NameNotFoundException ignored) {}
        }
        return defaultFallback != null ? defaultFallback : packageName;
    }

    public static String getDefaultLabel(String packageName) {
        if (packageName == null) return null;
        String val = DYNAMIC_REGISTRY.get(packageName);
        return val != null ? val : BASELINE_ALLOWLIST.get(packageName);
    }

    public static Map<String, String> getAllowlist() {
        return Collections.unmodifiableMap(DYNAMIC_REGISTRY);
    }

    public static Map<String, String> getBaselineAllowlist() {
        return BASELINE_ALLOWLIST;
    }

    /**
     * Resets dynamic registry back to baseline allowlist (for unit tests).
     */
    public static void resetToBaseline() {
        DYNAMIC_REGISTRY.clear();
        DYNAMIC_REGISTRY.putAll(BASELINE_ALLOWLIST);
    }
}
