using System;
using System.Collections.Generic;
using RMX3171ControlCentre.Models;

namespace RMX3171ControlCentre.Services.Security
{
    /// <summary>
    /// Classifies Android processes and packages into risk levels for the Memory Manager.
    ///
    /// CLASSIFICATION RULES (applied in order):
    /// 1. If process name has NO dots → it is a native daemon → PROTECTED
    /// 2. Known-safe third-party apps → SAFE_USER
    /// 3. Strictly-protected exact package names → PROTECTED
    /// 4. com.android.* / com.google.android.* → PROTECTED (core OS/Google)
    /// 5. com.coloros.* / com.oplus.* / com.oppo.* / com.realme.* / com.nearme.*
    ///    / com.heytap.* / com.mediatek.* / se.dirac.* → PROTECTED (vendor)
    /// 6. org.telegram.* or similar well-known third-party with dotted name → USER
    /// 7. Any dotted name installed by user (isSystem=false) → USER
    /// 8. Any dotted name flagged isSystem=true → SYSTEM
    /// 9. Anything else → UNKNOWN (defaults to PROTECTED behavior)
    /// </summary>
    public static class PackageRiskEvaluator
    {
        // ─── Exact package names that are always PROTECTED ─────────────────────────
        private static readonly HashSet<string> StrictlyProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "android",
            "com.android.systemui",
            "com.android.settings",
            "com.android.phone",
            "com.android.server.telecom",
            "com.android.vending",
            "com.android.se",
            "com.coloros.safecenter",
            "com.oplus.safecenter",
            "com.oppo.launcher",
            "com.realme.launcher",
            "com.coloros.launcher",
            "com.google.android.gms",
            "com.google.android.gms.persistent",
            "com.google.android.gsf",
            "com.google.android.webview",
            "com.google.process.gservices",
            "com.google.android.ext.services",
            "com.heytap.mcs",
            "com.oplus.battery",
            "com.coloros.battery",
            "com.coloros.gesture",
            "com.oppo.nhs",
            "com.coloros.deepthinker",
            "com.coloros.athena",
            "android.process.acore",
            "com.coloros.persist.system",
            "com.coloros.persist.multimedia",
            "com.google.android.providers.media.module",
            "com.heytap.appplatform",
            "com.heytap.openid",
            "com.coloros.exsystemservice",
            "com.coloros.exserviceui",
            "com.coloros.securitypermission",
            "com.coloros.sau",
            "com.nearme.romupdate",
            "com.coloros.weather.service",
            "com.oplus.onetrace",
            "com.oplus.customize.coreapp",
            "com.mediatek.ims",
            "com.mediatek.smartratswitch",
            "com.mediatek.voicecommand",
            "com.android.networkstack.process",
            "com.oppo.multimedia.dirac",
            "se.dirac.acs",
            "com.google.android.googlequicksearchbox",
            "com.google.android.inputmethod.latin",
            "com.google.android.apps.messaging",
        };

        // ─── Known safe user-installed (third-party) apps ──────────────────────────
        private static readonly HashSet<string> KnownSafeUserApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.whatsapp",
            "com.android.chrome",
            "com.instagram.android",
            "com.facebook.katana",
            "com.twitter.android",
            "com.twitter.android.lite",
            "org.mozilla.firefox",
            "com.spotify.music",
            "org.telegram.messenger",
            "com.discord",
            "com.snapchat.android",
            "com.openai.chatgpt",
            "com.google.android.apps.bard",
            "com.google.android.youtube",
            "com.google.android.apps.docs",
            "com.google.android.apps.authenticator2",
            "com.tiktok.musically",
            "com.reddit.frontpage",
            "com.netflix.mediaclient",
            "com.google.android.apps.paidtasks",
        };

        // ─── Vendor/OEM package prefixes → always PROTECTED ───────────────────────
        private static readonly string[] VendorPrefixes = new[]
        {
            "com.coloros.",
            "com.oplus.",
            "com.oppo.",
            "com.realme.",
            "com.nearme.",
            "com.heytap.",
            "com.mediatek.",
            "se.dirac.",
            "com.qualcomm.",
            "com.mtk.",
        };

        // ─── Known native daemon names (no dots, lower-case) ─────────────────────
        // These are process names, NOT package names. They must never be force-stopped.
        private static readonly HashSet<string> KnownNativeDaemons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core Android kernel/native
            "init", "ueventd", "logd", "lmkd", "vold", "netd", "installd",
            "servicemanager", "hwservicemanager", "vndservicemanager", "storaged",
            "keystore", "gatekeeperd", "credstore", "incidentd", "tombstoned",
            "statsd", "traced", "traced_probes", "drmserver", "mediadrmserver",
            "zygote", "zygote64", "webview_zygote", "app_process", "app_process64",
            // Graphics
            "surfaceflinger", "gpuservice",
            // Audio/Media
            "audioserver", "mediaserver", "cameraserver", "camerasloganserver",
            "camerahalserver",
            // Codecs
            "media.codec", "media.extractor", "media.swcodec", "media.metrics",
            "android.hardware.audio.service.mediatek",
            // WiFi/Network
            "wificond", "wpa_supplicant", "mdnsd", "charon", "ipsec_mon", "netdiag", "netdagent",
            // Telephony/Radio
            "rild", "mtkfusionrild", "gsm0710muxd", "volte_stack", "volte_ua",
            "volte_imcb", "volte_imsm_93", "volte_md_status", "epdg_wod",
            "bip", "vtservice", "vtservice_hidl",
            // Bluetooth/Location
            "android.hardware.bluetooth@1.0-service-mediatek",
            "wmt_launcher", "lbs_hidl_service", "lbs_dbg", "mnld", "mtk_agpsd",
            // Vendor/MTK HAL
            "vpud", "oiface", "horae", "adsprpcd", "atlasservice",
            "ccci_fsd", "ccci_mdinit", "ccci_rpcd", "emdlogger1",
            "mobile_log_d", "connsyslogger", "stp_dump3",
            // Debugging/Logging
            "aee_aed", "aee_aedv", "aee_aed64", "aee_aedv64",
            "criticallog", "logcat", "tombstoned",
            // System utilities
            "sh", "sleep", "init", "adbd", "dumpsys", "starter",
            "batterywarning", "fuelgauged", "thermald", "thermal", "thermalloadalgod",
            "wlan_assistant", "oplus_kevent", "hans", "oppo_theia", "ppl_agent",
            "giftserver", "common_dcs", "iptables-restore", "ip6tables-restore",
            "mcDriverDaemon",
        };

        /// <summary>
        /// Returns true when the string looks like a native process name:
        /// no dots, no package structure.
        /// e.g. "surfaceflinger", "zygote", "netd", "media.codec"
        /// Note: "media.codec" has a dot but is still a native process — we detect it
        /// through the KnownNativeDaemons set.
        /// </summary>
        private static bool LooksLikeNativeProcess(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            // No dots = definitely native binary, not a Java package
            if (!name.Contains('.')) return true;
            // HAL service pattern: contains @ or starts with android.hardware / vendor.
            if (name.StartsWith("android.hardware.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("android.system.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("android.hidl.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("vendor.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("se.", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("se.dirac.acs")) return true;
            return false;
        }

        /// <summary>
        /// Main classification entry point.
        /// </summary>
        public static PackageRiskLevel Evaluate(string processName, bool isSystem)
        {
            if (string.IsNullOrWhiteSpace(processName)) return PackageRiskLevel.UNKNOWN;
            processName = processName.Trim();

            // 1. Native daemon / HAL process → always PROTECTED
            if (LooksLikeNativeProcess(processName) || KnownNativeDaemons.Contains(processName))
                return PackageRiskLevel.PROTECTED;

            // 2. Exact known safe user apps → SAFE_USER
            if (KnownSafeUserApps.Contains(processName))
                return PackageRiskLevel.SAFE_USER;

            // 3. Strictly protected exact packages → PROTECTED
            if (StrictlyProtected.Contains(processName))
                return PackageRiskLevel.PROTECTED;

            // 4. Core Android packages → PROTECTED
            if (processName.StartsWith("com.android.", StringComparison.OrdinalIgnoreCase) ||
                processName.StartsWith("com.google.android.", StringComparison.OrdinalIgnoreCase))
                return PackageRiskLevel.PROTECTED;

            // 5. Vendor/OEM prefixes → PROTECTED
            foreach (var prefix in VendorPrefixes)
            {
                if (processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return PackageRiskLevel.PROTECTED;
            }

            // 6. System-flagged packages → SYSTEM
            if (isSystem)
                return PackageRiskLevel.SYSTEM;

            // 7. Dotted name, not system → USER
            if (processName.Contains('.'))
                return PackageRiskLevel.USER;

            // 8. Anything else → UNKNOWN (protected by default)
            return PackageRiskLevel.UNKNOWN;
        }

        /// <summary>
        /// Returns true only for packages where Force Stop is genuinely safe to expose.
        /// SAFE_USER and USER packages only. Everything else is protected.
        /// </summary>
        public static bool IsDestructiveActionAllowed(PackageRiskLevel risk)
        {
            return risk == PackageRiskLevel.USER || risk == PackageRiskLevel.SAFE_USER;
        }

        /// <summary>
        /// Determines whether this process name refers to a valid Android app package
        /// (not a native binary). Package names must contain dots and not look like
        /// native daemons.
        /// </summary>
        public static bool IsValidPackageName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!name.Contains('.')) return false;
            if (LooksLikeNativeProcess(name)) return false;
            if (KnownNativeDaemons.Contains(name)) return false;
            return true;
        }

        /// <summary>
        /// Returns a human-readable explanation of the classification.
        /// </summary>
        public static string GetRiskExplanation(PackageRiskLevel level, string processName)
        {
            if (!IsValidPackageName(processName))
                return "Native system process — not an Android application package.";

            return level switch
            {
                PackageRiskLevel.SAFE_USER => "User-installed application. Eligible for Force Stop.",
                PackageRiskLevel.USER => "User application with background services. Force Stop will close all processes.",
                PackageRiskLevel.SYSTEM => "System application. Cannot be force-stopped via Memory Cleaner.",
                PackageRiskLevel.PROTECTED => "Protected system or vendor component. Do not stop.",
                PackageRiskLevel.UNKNOWN => "Unclassified process. Defaulting to protected to ensure safety.",
                _ => "Unknown classification."
            };
        }
    }
}
