using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Adb;
using RMX3171ControlCentre.Services.Security;

namespace RMX3171ControlCentre.Services.Device
{
    public interface IMemoryService
    {
        Task<MemoryInfo> GetMemoryInfoAsync();
        Task<MemoryQueryResult> GetProcessesAsync();
        Task<bool> ForceStopAppAsync(string packageName);
    }

    /// <summary>
    /// Holds the result of a full memory scan, split into user apps and system processes.
    /// </summary>
    public class MemoryQueryResult
    {
        /// <summary>
        /// User applications — eligible for Force Stop (after Advanced Mode check).
        /// </summary>
        public List<AppProcessInfo> UserApps { get; set; } = new List<AppProcessInfo>();

        /// <summary>
        /// System, vendor, native, and unclassified processes — read-only diagnostic.
        /// </summary>
        public List<AppProcessInfo> SystemProcesses { get; set; } = new List<AppProcessInfo>();

        /// <summary>
        /// The data sources used for this query (for display in the UI).
        /// </summary>
        public string Sources { get; set; } = "";

        /// <summary>
        /// Total PSS of all user apps in MB.
        /// </summary>
        public double UserAppsTotal => UserApps.Sum(a => a.RamMb);

        /// <summary>
        /// Total PSS of all system processes in MB.
        /// </summary>
        public double SystemTotal => SystemProcesses.Sum(a => a.RamMb);
    }

    public class MemoryService : IMemoryService
    {
        private readonly IAdbService _adbService;
        private readonly ILogService _logService;

        public MemoryService(IAdbService adbService, ILogService logService)
        {
            _adbService = adbService;
            _logService = logService;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GetMemoryInfoAsync — /proc/meminfo + /proc/swaps for ZRAM
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<MemoryInfo> GetMemoryInfoAsync()
        {
            var memInfo = new MemoryInfo();

            var memInfoResult = await _adbService.ExecuteCommandAsync("shell cat /proc/meminfo");
            if (memInfoResult.ExitCode == 0)
            {
                foreach (var line in memInfoResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && double.TryParse(parts[1], out double kb))
                    {
                        double gb = kb / (1024.0 * 1024.0);
                        switch (parts[0])
                        {
                            case "MemTotal":     memInfo.TotalGB     = gb; break;
                            case "MemFree":      memInfo.FreeGB      = gb; break;
                            case "MemAvailable": memInfo.AvailableGB = gb; break;
                            case "Cached":       memInfo.CachedGB    = gb; break;
                        }
                    }
                }
                memInfo.UsedGB = Math.Max(0, memInfo.TotalGB - memInfo.AvailableGB);
            }

            // ZRAM via /proc/swaps
            var swapResult = await _adbService.ExecuteCommandAsync("shell cat /proc/swaps");
            if (swapResult.ExitCode == 0)
            {
                foreach (var line in swapResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("/dev/block/zram"))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            if (double.TryParse(parts[2], out double sizeKb))
                                memInfo.ZramTotalGB = sizeKb / (1024.0 * 1024.0);
                            if (double.TryParse(parts[3], out double usedKb))
                                memInfo.ZramUsedGB = usedKb / (1024.0 * 1024.0);
                        }
                    }
                }
            }

            // Memory pressure: use dumpsys meminfo status line, not free-RAM percentage
            var meminfoResult = await _adbService.ExecuteCommandAsync("shell dumpsys meminfo");
            if (meminfoResult.ExitCode == 0)
            {
                var statusLine = meminfoResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.Contains("Total RAM:") && l.Contains("status"));
                if (statusLine != null)
                {
                    var statusMatch = Regex.Match(statusLine, @"status\s+(\w+)");
                    if (statusMatch.Success)
                    {
                        var status = statusMatch.Groups[1].Value;
                        memInfo.MemoryPressure = char.ToUpper(status[0]) + status.Substring(1).ToLower();
                    }
                }
            }

            if (string.IsNullOrEmpty(memInfo.MemoryPressure))
                memInfo.MemoryPressure = "Unknown";

            return memInfo;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GetProcessesAsync — combined dumpsys meminfo OOM + pm list packages
        //
        // DATA SOURCE: "Total PSS by OOM adjustment:" section of dumpsys meminfo.
        // This section lists every running process grouped by Android lifecycle state:
        //   Native, System, Persistent, Foreground, Visible, Perceptible,
        //   A Services, B Services, Cached.
        //
        // CROSS-REFERENCE: pm list packages -3 gives the set of user-installed
        // (third-party) package names. We use this to validate that an entry in
        // the meminfo output corresponds to a real installed APK, not a native binary.
        //
        // METRIC: PSS (Proportional Set Size) — accounts for shared memory pages
        // proportionally. It understates true RAM impact compared to RSS but is the
        // most accurate per-process metric Android exposes via this API.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<MemoryQueryResult> GetProcessesAsync()
        {
            var result = new MemoryQueryResult
            {
                Sources = "ActivityManager (dumpsys meminfo) + PackageManager (pm list packages)"
            };

            // Step 1: Get list of user-installed package names for cross-referencing
            var userPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -3");
            if (pmResult.ExitCode == 0)
            {
                foreach (var line in pmResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pkg = line.Replace("package:", "").Trim();
                    if (!string.IsNullOrEmpty(pkg))
                        userPackages.Add(pkg);
                }
            }

            // Step 2: Parse dumpsys meminfo OOM adjustment section
            var meminfoResult = await _adbService.ExecuteCommandAsync("shell dumpsys meminfo");
            if (meminfoResult.ExitCode != 0)
                return result;

            var lines = meminfoResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            bool inOomSection = false;
            string currentOomCategory = "Unknown";

            // Process line: "        282,357K: com.instagram.android (pid 2252)"
            // Also matches:  "         62,670K: media.codec (pid 1200)"
            var processRegex = new Regex(@"^\s{6,}([\d,]+)\s*K:\s+([^\s(]+)\s*\(pid\s+\d+");
            // Category line: "    340,205K: Foreground"  (indented 4-6 spaces, no parenthesis)
            var categoryRegex = new Regex(@"^\s{2,6}([\d,]+)\s*K:\s+([A-Za-z][A-Za-z\s]+)$");

            // Aggregated by base package name
            var byPackage = new Dictionary<string, AppProcessInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                if (line.Contains("Total PSS by OOM adjustment:"))
                {
                    inOomSection = true;
                    continue;
                }
                if (inOomSection && line.Contains("Total PSS by category:"))
                    break;

                if (!inOomSection) continue;

                // Try category first (less indented)
                var catMatch = categoryRegex.Match(line);
                if (catMatch.Success)
                {
                    currentOomCategory = catMatch.Groups[2].Value.Trim();
                    continue;
                }

                // Then try process line
                var procMatch = processRegex.Match(line);
                if (!procMatch.Success) continue;

                string kbStr      = procMatch.Groups[1].Value.Replace(",", "");
                string processName = procMatch.Groups[2].Value.Trim();

                if (!double.TryParse(kbStr, out double kbVal)) continue;
                double mb = kbVal / 1024.0;

                // Strip :suffix (e.g., com.instagram.android:fbns → com.instagram.android)
                string basePackage = processName.Contains(':')
                    ? processName.Substring(0, processName.IndexOf(':'))
                    : processName;

                // Classify
                bool isPmUserApp = userPackages.Contains(basePackage);
                var risk = PackageRiskEvaluator.Evaluate(basePackage, !isPmUserApp);

                // Aggregate multiple processes of the same app
                if (byPackage.TryGetValue(basePackage, out var existing))
                {
                    existing.RamMb += mb;
                    // Upgrade importance category if this process is "more foreground"
                    if (ImportanceRank(currentOomCategory) > ImportanceRank(existing.Importance))
                        existing.Importance = currentOomCategory;
                }
                else
                {
                    bool canStop = PackageRiskEvaluator.IsDestructiveActionAllowed(risk)
                                   && PackageRiskEvaluator.IsValidPackageName(basePackage);

                    byPackage[basePackage] = new AppProcessInfo
                    {
                        PackageName = basePackage,
                        AppName     = basePackage,
                        RamMb       = mb,
                        Importance  = currentOomCategory,
                        IsProtected = !canStop,
                        RiskLevel   = risk,
                        CanForceStop = canStop,
                    };
                }
            }

            // Step 3: Split into user vs system buckets
            foreach (var proc in byPackage.Values.OrderByDescending(p => p.RamMb))
            {
                bool isUserApp = proc.RiskLevel == PackageRiskLevel.SAFE_USER
                              || proc.RiskLevel == PackageRiskLevel.USER;

                // Additional guard: must be a real package AND registered as user-installed
                // OR we are confident it's a third-party app based on pm -3
                if (isUserApp && PackageRiskEvaluator.IsValidPackageName(proc.PackageName))
                    result.UserApps.Add(proc);
                else
                    result.SystemProcesses.Add(proc);
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ForceStopAppAsync
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<bool> ForceStopAppAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            // Safety: refuse to force-stop native processes
            if (!PackageRiskEvaluator.IsValidPackageName(packageName))
            {
                _logService.LogWarning($"ForceStop refused: '{packageName}' is not a valid package name.");
                return false;
            }

            var risk = PackageRiskEvaluator.Evaluate(packageName, false);
            if (!PackageRiskEvaluator.IsDestructiveActionAllowed(risk))
            {
                _logService.LogWarning($"ForceStop refused: '{packageName}' is classified as {risk}.");
                return false;
            }

            _logService.LogDebug($"Force-stop: {packageName}");
            var result = await _adbService.ExecuteCommandAsync($"shell am force-stop {packageName}", isReadOnly: false);
            _logService.LogDebug($"Force-stop result for {packageName}: exit={result.ExitCode}");
            return result.ExitCode == 0;
        }

        /// <summary>
        /// Returns a numeric rank for OOM categories so we can prefer the most
        /// "foreground" state when aggregating multi-process apps.
        /// Higher = more foreground.
        /// </summary>
        private static int ImportanceRank(string category) => category switch
        {
            "Foreground"          => 100,
            "Visible"             => 90,
            "Perceptible"         => 80,
            "Persistent"          => 70,
            "Persistent Service"  => 65,
            "A Services"          => 60,
            "Previous"            => 50,
            "B Services"          => 40,
            "Cached"              => 30,
            "System"              => 20,
            "Native"              => 10,
            _                     => 0,
        };
    }
}
