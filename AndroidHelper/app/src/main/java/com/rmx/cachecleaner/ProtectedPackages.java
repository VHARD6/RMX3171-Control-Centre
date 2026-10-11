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
        map.put("moe.shizuku.privileged.api", "Shizuku privilege bridge (prevents terminating active IPC connection)");
        map.put("android", "Core Android OS framework");
        map.put("oplus", "Core ColorOS/Oppo OS platform framework");
        map.put("com.android.settings", "Core system Settings application (host for cache cleaning automation)");
        map.put("com.android.systemui", "System UI (manages status bar, navigation bar, and lock screen)");
        map.put("com.google.android.packageinstaller", "System package installer and uninstaller");
        map.put("com.android.packageinstaller", "System package installer fallback");
        map.put("com.google.android.permissioncontroller", "Android runtime permission controller");
        map.put("com.android.phone", "Core Telephony and Radio phone service");
        map.put("com.android.server.telecom", "Core Telecom call management framework");
        map.put("com.android.providers.telephony", "Telephony and SMS/MMS system provider");
        map.put("com.mediatek.telephony", "Chipset telephony provider");
        map.put("com.google.android.inputmethod.latin", "Active input method editor (Gboard keyboard)");
        map.put("com.google.android.marvin.talkback", "Active accessibility infrastructure (TalkBack)");
        map.put("com.android.providers.settings", "System settings database provider");
        map.put("com.coloros.wirelesssettings", "ColorOS wireless and network settings service");
        map.put("com.coloros.simsettings", "ColorOS SIM card management service");
        map.put("com.coloros.launcher", "ColorOS Home Launcher (desktop launcher interface)");
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
