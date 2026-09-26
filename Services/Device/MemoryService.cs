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

    public class MemoryQueryResult
    {
        public List<AppProcessInfo> UserApps { get; set; } = new List<AppProcessInfo>();
        public List<AppProcessInfo> VendorOptional { get; set; } = new List<AppProcessInfo>();
        public List<AppProcessInfo> SystemProcesses { get; set; } = new List<AppProcessInfo>();
        public string Sources { get; set; } = "";
        
        public double UserAppsTotal => UserApps.Sum(a => a.RamMb);
        public double VendorTotal => VendorOptional.Sum(a => a.RamMb);
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

        public async Task<MemoryQueryResult> GetProcessesAsync()
        {
            var result = new MemoryQueryResult
            {
                Sources = "ActivityManager + PackageManager"
            };

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

            var meminfoResult = await _adbService.ExecuteCommandAsync("shell dumpsys meminfo");
            if (meminfoResult.ExitCode != 0)
                return result;

            var lines = meminfoResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            bool inOomSection = false;
            string currentOomCategory = "Unknown";

            var processRegex = new Regex(@"^\s{6,}([\d,]+)\s*K:\s+([^\s(]+)\s*\(pid\s+\d+");
            var categoryRegex = new Regex(@"^\s{2,6}([\d,]+)\s*K:\s+([A-Za-z][A-Za-z\s]+)$");

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

                var catMatch = categoryRegex.Match(line);
                if (catMatch.Success)
                {
                    currentOomCategory = catMatch.Groups[2].Value.Trim();
                    continue;
                }

                var procMatch = processRegex.Match(line);
                if (!procMatch.Success) continue;

                string kbStr      = procMatch.Groups[1].Value.Replace(",", "");
                string processName = procMatch.Groups[2].Value.Trim();

                if (!double.TryParse(kbStr, out double kbVal)) continue;
                double mb = kbVal / 1024.0;

                string basePackage = processName.Contains(':')
                    ? processName.Substring(0, processName.IndexOf(':'))
                    : processName;

                bool isPmUserApp = userPackages.Contains(basePackage);
                var classification = PackageRiskEvaluator.Evaluate(basePackage, !isPmUserApp);

                if (byPackage.TryGetValue(basePackage, out var existing))
                {
                    existing.RamMb += mb;
                    if (ImportanceRank(currentOomCategory) > ImportanceRank(existing.Importance))
                        existing.Importance = currentOomCategory;
                }
                else
                {
                    byPackage[basePackage] = new AppProcessInfo
                    {
                        PackageName = basePackage,
                        AppName     = basePackage,
                        RamMb       = mb,
                        Importance  = currentOomCategory,
                        RiskLevel   = classification.RiskLevel,
                        Recommendation = classification.Recommendation,
                        Reason = classification.Reason
                    };
                }
            }

            foreach (var proc in byPackage.Values.OrderByDescending(p => p.RamMb))
            {
                if (proc.RiskLevel == PackageRiskLevel.LOW && PackageRiskEvaluator.IsValidPackageName(proc.PackageName))
                {
                    result.UserApps.Add(proc);
                }
                else if (proc.RiskLevel == PackageRiskLevel.MODERATE && PackageRiskEvaluator.IsValidPackageName(proc.PackageName))
                {
                    result.VendorOptional.Add(proc);
                }
                else
                {
                    result.SystemProcesses.Add(proc);
                }
            }

            return result;
        }

        public async Task<bool> ForceStopAppAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            if (!PackageRiskEvaluator.IsValidPackageName(packageName))
            {
                _logService.LogWarning($"ForceStop refused: '{packageName}' is not a valid package name.");
                return false;
            }

            var classification = PackageRiskEvaluator.Evaluate(packageName, false);
            if (classification.RiskLevel == PackageRiskLevel.CRITICAL)
            {
                _logService.LogWarning($"ForceStop refused: '{packageName}' is classified as CRITICAL.");
                return false;
            }

            _logService.LogDebug($"Force-stop: {packageName}");
            var result = await _adbService.ExecuteCommandAsync($"shell am force-stop {packageName}", isReadOnly: false);
            _logService.LogDebug($"Force-stop result for {packageName}: exit={result.ExitCode}");
            return result.ExitCode == 0;
        }

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
