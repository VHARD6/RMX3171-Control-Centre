using System;
using System.Collections.Generic;
using RMX3171ControlCentre.Models;

namespace RMX3171ControlCentre.Services.Security
{
    public class PackageClassification
    {
        public PackageRiskLevel RiskLevel { get; set; } = PackageRiskLevel.UNKNOWN;
        public PackageRecommendation Recommendation { get; set; } = PackageRecommendation.UNKNOWN;
        public string Reason { get; set; } = string.Empty;
        public string Confidence { get; set; } = "Low";
    }

    public static class PackageRiskEvaluator
    {
        // ─── CRITICAL PACKAGES (DO NOT REMOVE) ─────────────────────────
        private static readonly HashSet<string> CriticalPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            "android.process.acore",
            "com.coloros.persist.system",
            "com.coloros.persist.multimedia",
            "com.google.android.providers.media.module",
            "com.coloros.exsystemservice",
            "com.coloros.exserviceui",
            "com.coloros.securitypermission",
            "com.coloros.sau",
            "com.nearme.romupdate",
            "com.oplus.customize.coreapp",
            "com.mediatek.ims",
            "com.mediatek.smartratswitch",
            "com.mediatek.voicecommand",
            "com.android.networkstack.process",
            "com.oppo.multimedia.dirac",
            "se.dirac.acs",
            "com.google.android.inputmethod.latin",
            "com.google.android.apps.messaging",
        };

        // ─── HIGH RISK PACKAGES ──────────────────────────
        private static readonly HashSet<string> HighRiskPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.heytap.mcs",
            "com.oplus.battery",
            "com.coloros.battery",
            "com.coloros.gesture",
            "com.oppo.nhs",
            "com.coloros.deepthinker",
            "com.coloros.athena",
            "com.heytap.appplatform",
            "com.heytap.openid",
            "com.coloros.weather.service",
            "com.oplus.onetrace",
            "com.google.android.googlequicksearchbox",
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

        // ─── Vendor/OEM package prefixes ───────────────────────
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
        private static readonly HashSet<string> KnownNativeDaemons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "init", "ueventd", "logd", "lmkd", "vold", "netd", "installd",
            "servicemanager", "hwservicemanager", "vndservicemanager", "storaged",
            "keystore", "gatekeeperd", "credstore", "incidentd", "tombstoned",
            "statsd", "traced", "traced_probes", "drmserver", "mediadrmserver",
            "zygote", "zygote64", "webview_zygote", "app_process", "app_process64",
            "surfaceflinger", "gpuservice",
            "audioserver", "mediaserver", "cameraserver", "camerasloganserver", "camerahalserver",
            "media.codec", "media.extractor", "media.swcodec", "media.metrics",
            "android.hardware.audio.service.mediatek",
            "wificond", "wpa_supplicant", "mdnsd", "charon", "ipsec_mon", "netdiag", "netdagent",
            "rild", "mtkfusionrild", "gsm0710muxd", "volte_stack", "volte_ua",
            "volte_imcb", "volte_imsm_93", "volte_md_status", "epdg_wod",
            "bip", "vtservice", "vtservice_hidl",
            "android.hardware.bluetooth@1.0-service-mediatek",
            "wmt_launcher", "lbs_hidl_service", "lbs_dbg", "mnld", "mtk_agpsd",
            "vpud", "oiface", "horae", "adsprpcd", "atlasservice",
            "ccci_fsd", "ccci_mdinit", "ccci_rpcd", "emdlogger1",
            "mobile_log_d", "connsyslogger", "stp_dump3",
            "aee_aed", "aee_aedv", "aee_aed64", "aee_aedv64",
            "criticallog", "logcat", "tombstoned",
            "sh", "sleep", "adbd", "dumpsys", "starter",
            "batterywarning", "fuelgauged", "thermald", "thermal", "thermalloadalgod",
            "wlan_assistant", "oplus_kevent", "hans", "oppo_theia", "ppl_agent",
            "giftserver", "common_dcs", "iptables-restore", "ip6tables-restore",
            "mcDriverDaemon",
        };

        private static bool LooksLikeNativeProcess(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (!name.Contains('.')) return true;
            if (name.StartsWith("android.hardware.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("android.system.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("android.hidl.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("vendor.", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("se.", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("se.dirac.acs")) return true;
            return false;
        }

        public static PackageClassification Evaluate(string processName, bool isSystem)
        {
            if (string.IsNullOrWhiteSpace(processName)) return new PackageClassification();
            processName = processName.Trim();

            // 1. Native daemon / HAL process
            if (LooksLikeNativeProcess(processName) || KnownNativeDaemons.Contains(processName))
                return new PackageClassification { RiskLevel = PackageRiskLevel.CRITICAL, Recommendation = PackageRecommendation.DO_NOT_REMOVE, Reason = "Native system daemon or HAL process. Do not stop.", Confidence = "High" };

            // 2. Exact CRITICAL packages
            if (CriticalPackages.Contains(processName))
                return new PackageClassification { RiskLevel = PackageRiskLevel.CRITICAL, Recommendation = PackageRecommendation.DO_NOT_REMOVE, Reason = "Critical Android or vendor infrastructure.", Confidence = "High" };

            // 3. Known safe user apps
            if (KnownSafeUserApps.Contains(processName))
                return new PackageClassification { RiskLevel = PackageRiskLevel.LOW, Recommendation = PackageRecommendation.REMOVE_CANDIDATE, Reason = "Known user application.", Confidence = "High" };

            // 4. Exact HIGH RISK packages (Often vendor bloat, but potentially relied on)
            if (HighRiskPackages.Contains(processName))
                return new PackageClassification { RiskLevel = PackageRiskLevel.MODERATE, Recommendation = PackageRecommendation.OPTIONAL_COMPONENT, Reason = "Vendor/OEM component. May affect specific ecosystem features if removed.", Confidence = "Medium" };

            // 5. Core Android packages (Generally CRITICAL/HIGH, maybe just HIGH for unknown ones)
            if (processName.StartsWith("com.android.", StringComparison.OrdinalIgnoreCase) ||
                processName.StartsWith("com.google.android.", StringComparison.OrdinalIgnoreCase))
                return new PackageClassification { RiskLevel = PackageRiskLevel.HIGH, Recommendation = PackageRecommendation.DO_NOT_REMOVE, Reason = "Core Android or Google component.", Confidence = "Medium" };

            // 6. Vendor/OEM prefixes -> Typically MODERATE unless it's a known CRITICAL one above.
            foreach (var prefix in VendorPrefixes)
            {
                if (processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new PackageClassification { RiskLevel = PackageRiskLevel.MODERATE, Recommendation = PackageRecommendation.OPTIONAL_COMPONENT, Reason = "Vendor service associated with OEM functionality.", Confidence = "Low" };
            }

            // 7. System-flagged packages -> HIGH risk, DO NOT REMOVE unless we know more
            if (isSystem)
                return new PackageClassification { RiskLevel = PackageRiskLevel.HIGH, Recommendation = PackageRecommendation.DO_NOT_REMOVE, Reason = "Unknown system application.", Confidence = "Low" };

            // 8. Dotted name, not system -> USER -> LOW risk
            if (processName.Contains('.'))
                return new PackageClassification { RiskLevel = PackageRiskLevel.LOW, Recommendation = PackageRecommendation.REMOVE_CANDIDATE, Reason = "User application.", Confidence = "High" };

            // 9. Anything else -> UNKNOWN
            return new PackageClassification { RiskLevel = PackageRiskLevel.UNKNOWN, Recommendation = PackageRecommendation.UNKNOWN, Reason = "Unrecognized process or package.", Confidence = "Low" };
        }

        public static bool IsValidPackageName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (!name.Contains('.')) return false;
            if (LooksLikeNativeProcess(name)) return false;
            if (KnownNativeDaemons.Contains(name)) return false;
            return true;
        }
    }
}
