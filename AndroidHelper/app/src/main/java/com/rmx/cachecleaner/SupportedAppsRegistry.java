package com.rmx.cachecleaner;

import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Phase 5: Supported Apps Registry.
 * Enforces strict allowlisting for third-party applications supported for private cache cleaning.
 * 
 * Safety Guarantees:
 * 1. Arbitrary packages from UI/intents are strictly rejected.
 * 2. System apps (android.*, com.android.*, com.coloros.*, com.oplus.*, com.heytap.*, etc.) are excluded.
 * 3. Only installed, allowlisted packages are exposed for user selection.
 */
public class SupportedAppsRegistry {

    // Predefined allowlist of supported 3rd-party applications
    private static final Map<String, String> ALLOWLIST;
    static {
        Map<String, String> map = new LinkedHashMap<>();
        map.put("com.instagram.android", "Instagram");
        map.put("com.spotify.music", "Spotify");
        map.put("com.snapchat.android", "Snapchat");
        map.put("com.grofers.customerapp", "Blinkit");
        ALLOWLIST = Collections.unmodifiableMap(map);
    }

    // Explicitly forbidden package prefixes to prevent system app manipulation
    private static final String[] FORBIDDEN_PREFIXES = {
            "android",
            "com.android.",
            "com.coloros.",
            "com.oplus.",
            "com.heytap.",
            "com.nearme.",
            "com.mediatek.",
            "com.google.android.packageinstaller",
            "com.rmx.cachecleaner"
    };

    /**
     * Checks if a package name is in the predefined allowlist and not in forbidden prefixes.
     */
    public static boolean isPackageAllowed(String packageName) {
        if (packageName == null || packageName.trim().isEmpty()) {
            return false;
        }
        packageName = packageName.trim();

        for (String prefix : FORBIDDEN_PREFIXES) {
            if (packageName.equals(prefix) || packageName.startsWith(prefix)) {
                return false;
            }
        }

        return ALLOWLIST.containsKey(packageName);
    }

    /**
     * Get list of supported apps that are currently installed on the device.
     */
    public static List<TargetApp> getInstalledSupportedApps(PackageManager pm) {
        List<TargetApp> installedApps = new ArrayList<>();
        if (pm == null) {
            return installedApps;
        }

        for (Map.Entry<String, String> entry : ALLOWLIST.entrySet()) {
            String pkg = entry.getKey();
            String defaultLabel = entry.getValue();

            try {
                ApplicationInfo appInfo = pm.getApplicationInfo(pkg, 0);
                if (appInfo != null) {
                    TargetApp app = new TargetApp(pkg, defaultLabel);
                    app.setInstalled(true);
                    CharSequence label = pm.getApplicationLabel(appInfo);
                    if (label != null && label.length() > 0) {
                        app.setResolvedLabel(label.toString().trim());
                    }
                    installedApps.add(app);
                }
            } catch (PackageManager.NameNotFoundException ignored) {
                // Not installed on this device - hide per requirements
            }
        }

        return installedApps;
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
            } catch (PackageManager.NameNotFoundException ignored) {
            }
        }
        return defaultFallback != null ? defaultFallback : packageName;
    }

    /**
     * Return default label for an allowlisted package.
     */
    public static String getDefaultLabel(String packageName) {
        return ALLOWLIST.get(packageName);
    }

    public static Map<String, String> getAllowlist() {
        return ALLOWLIST;
    }
}
