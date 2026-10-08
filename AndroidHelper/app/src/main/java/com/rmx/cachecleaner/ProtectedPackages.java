package com.rmx.cachecleaner;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Centralized, documented repository of protected packages that are strictly
 * excluded from cache cleaning operations to prevent IPC disruption, launcher redraws,
 * or self-invalidation.
 */
public final class ProtectedPackages {

    private static final Map<String, String> EXCLUSIONS;

    static {
        Map<String, String> map = new LinkedHashMap<>();
        map.put("com.rmx.cachecleaner", "Self companion application (prevents self-invalidation during cleaning)");
        map.put("moe.shizuku.privileged.api", "Shizuku privilege bridge (prevents closing active IPC connection)");
        map.put("android", "Core Android OS framework (virtual package without standard user cache)");
        map.put("com.android.systemui", "System UI (prevents notification shade / status bar / navigation reloading)");
        map.put("com.coloros.launcher", "ColorOS Home Launcher (prevents desktop launcher redraw and icon reload)");
        EXCLUSIONS = Collections.unmodifiableMap(map);
    }

    private ProtectedPackages() {}

    /**
     * Checks if a package is strictly protected from cache deletion.
     */
    public static boolean isExcluded(String packageName) {
        return packageName != null && EXCLUSIONS.containsKey(packageName);
    }

    /**
     * Retrieves the documented reason for a package's exclusion.
     */
    public static String getExclusionReason(String packageName) {
        return EXCLUSIONS.get(packageName);
    }

    /**
     * Returns an unmodifiable map of all protected packages and their exclusion reasons.
     */
    public static Map<String, String> getAllExclusions() {
        return EXCLUSIONS;
    }
}
