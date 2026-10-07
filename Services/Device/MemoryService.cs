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
        Task<bool> DisablePackageAsync(string packageName);
        Task<bool> EnablePackageAsync(string packageName);
        Task<System.Collections.Generic.List<RMX3171ControlCentre.Models.CacheAppInfo>> GetAppCachesAsync();
        Task<double> GetTotalCacheSizeMbAsync();
        Task<(string status, double clearedMb, double remainingMb)> ClearAppCacheAsync(string packageName);
    }

    public class MemoryQueryResult
    {
        public List<AppProcessInfo> UserApps { get; set; } = new List<AppProcessInfo>();
        public List<AppProcessInfo> VendorOptional { get; set; } = new List<AppProcessInfo>();
        public List<AppProcessInfo> SystemProcesses { get; set; } = new List<AppProcessInfo>();
        public string Sources { get; set; } = "";
        public HashSet<string> DisabledPackages { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<AppProcessInfo> DisabledAppProcesses { get; set; } = new List<AppProcessInfo>();
        
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
                    var parts = line.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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
                        var parts = line.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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
            
            var disabledPmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -d --user 0");
            if (disabledPmResult.ExitCode == 0)
            {
                var disabledPackages = disabledPmResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Replace("package:", "").Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();

                foreach (var pkg in disabledPackages)
                {
                    result.DisabledPackages.Add(pkg);
                    
                    var stateResult = await _adbService.ExecuteCommandAsync($"shell \"dumpsys package {pkg} | grep -E -m 1 'enabled=[0-9]' | grep -E -o 'enabled=[0-9]'\"");
                    string stateStr = stateResult.Output.Trim();
                    string stateDetail = stateStr == "enabled=3" ? "DISABLED_USER" : "DISABLED";
                    
                    bool isPmUserApp = userPackages.Contains(pkg);
                    var classification = PackageRiskEvaluator.Evaluate(pkg, !isPmUserApp);
                    
                    var info = new AppProcessInfo
                    {
                        PackageName = pkg,
                        AppName = pkg,
                        IsDisabled = true,
                        DisabledStateDetail = stateDetail,
                        RiskLevel = classification.RiskLevel,
                        Recommendation = classification.Recommendation,
                        Reason = classification.Reason
                    };
                    result.DisabledAppProcesses.Add(info);
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

        public async Task<bool> DisablePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            var classification = PackageRiskEvaluator.Evaluate(packageName, false);
            if (classification.RiskLevel == PackageRiskLevel.CRITICAL) return false;
            
            var result = await _adbService.ExecuteCommandAsync($"shell pm disable-user --user 0 {packageName}", isReadOnly: false);
            return result.ExitCode == 0;
        }

        public async Task<bool> EnablePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            var result = await _adbService.ExecuteCommandAsync($"shell pm enable {packageName}", isReadOnly: false);
            return result.ExitCode == 0;
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
    
        public async Task<List<CacheAppInfo>> GetAppCachesAsync()
        {
            var result = new List<CacheAppInfo>();
            
            // Get all packages to determine user/system
            var pm3Result = await _adbService.ExecuteCommandAsync("shell pm list packages -3");
            var userPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pm3Result.ExitCode == 0)
            {
                foreach (var line in pm3Result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    userPkgs.Add(line.Replace("package:", "").Trim());
            }

            var pmSysResult = await _adbService.ExecuteCommandAsync("shell pm list packages -s");
            var sysPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pmSysResult.ExitCode == 0)
            {
                foreach (var line in pmSysResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    sysPkgs.Add(line.Replace("package:", "").Trim());
            }

            // Get external cache sizes
            var duResult = await _adbService.ExecuteCommandAsync("shell \"du -s /sdcard/Android/data/*/cache 2>/dev/null\"");
            var cacheSizes = new Dictionary<string, double>();
            if (duResult.ExitCode == 0)
            {
                foreach (var line in duResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        if (long.TryParse(parts[0], out long kb))
                        {
                            var pathParts = parts[1].Split('/');
                            if (pathParts.Length >= 5)
                            {
                                string pkg = pathParts[4];
                                cacheSizes[pkg] = kb / 1024.0;
                            }
                        }
                    }
                }
            }

            var allPkgs = new HashSet<string>(userPkgs);
            allPkgs.UnionWith(sysPkgs);
            allPkgs.UnionWith(cacheSizes.Keys);

            foreach (var pkg in allPkgs)
            {
                if (string.IsNullOrEmpty(pkg)) continue;

                bool isUserApp = userPkgs.Contains(pkg) && !sysPkgs.Contains(pkg);
                var risk = PackageRiskEvaluator.Evaluate(pkg, !isUserApp);
                
                string category = "USER APP";
                bool eligible = true;
                string reason = "";

                if (pkg.Equals("com.heytap.appplatform", StringComparison.OrdinalIgnoreCase) || risk.RiskLevel == PackageRiskLevel.CRITICAL)
                {
                    category = "CRITICAL SYSTEM COMPONENT";
                    eligible = false;
                    reason = "Critical dependency. Cannot be modified.";
                }
                else if (!isUserApp)
                {
                    if (risk.RiskLevel == PackageRiskLevel.MODERATE)
                    {
                        category = "VENDOR/OEM APP";
                        eligible = false;
                        reason = "Vendor app. Cache clearing restricted.";
                    }
                    else
                    {
                        category = "SYSTEM APP";
                        eligible = false;
                        reason = "System app. Cache clearing restricted.";
                    }
                }

                double size = cacheSizes.ContainsKey(pkg) ? cacheSizes[pkg] : 0.0;
                // Include if it has cache OR is an eligible user app so we can attempt to clear internal cache
                if (size > 0 || eligible)
                {
                    result.Add(new CacheAppInfo
                    {
                        PackageName = pkg,
                        AppName = pkg,
                        CacheSizeMb = size,
                        Category = category,
                        IsEligible = eligible,
                        ExcludeReason = reason
                    });
                }
            }
            return result.OrderByDescending(x => x.CacheSizeMb).ThenBy(x => x.PackageName).ToList();
        }

        public async Task<double> GetTotalCacheSizeMbAsync()
        {
            var res = await _adbService.ExecuteCommandAsync("shell dumpsys diskstats");
            if (res.ExitCode == 0)
            {
                var match = System.Text.RegularExpressions.Regex.Match(res.Output, @"App Cache Size: (\d+)");
                if (match.Success && long.TryParse(match.Groups[1].Value, out long bytes))
                {
                    return bytes / (1024.0 * 1024.0);
                }
            }
            return 0;
        }

        public async Task<(string status, double clearedMb, double remainingMb)> ClearAppCacheAsync(string packageName)
        {
            var checkRes = await _adbService.ExecuteCommandAsync($"shell \"[ -d /sdcard/Android/data/{packageName}/cache ] && echo YES || echo NO\"");
            if (!checkRes.Output.Contains("YES"))
            {
                return ("External cache unavailable", 0, 0);
            }

            var (beforeSuccess, beforeKb) = await GetDirectorySizeKbAsync($"/sdcard/Android/data/{packageName}/cache");
            if (!beforeSuccess)
            {
                return ("Failed", 0, 0);
            }

            if (beforeKb <= 8)
            {
                return ("Nothing to clear", 0, beforeKb / 1024.0);
            }

            var rmRes = await _adbService.ExecuteCommandAsync($"shell \"rm -rf /sdcard/Android/data/{packageName}/cache/*\"");
            if (rmRes.ExitCode != 0)
            {
                return ("Failed", 0, beforeKb / 1024.0);
            }

            var (afterSuccess, afterKb) = await GetDirectorySizeKbAsync($"/sdcard/Android/data/{packageName}/cache");
            if (!afterSuccess)
            {
                var checkDirAfter = await _adbService.ExecuteCommandAsync($"shell \"[ -d /sdcard/Android/data/{packageName}/cache ] && echo YES || echo NO\"");
                if (!checkDirAfter.Output.Contains("YES"))
                {
                    double clearedMbDisappeared = beforeKb / 1024.0;
                    return ("Cleared", clearedMbDisappeared, 0);
                }

                return ("Measurement failed", 0, 0);
            }

            double clearedMb = Math.Max(0, beforeKb - afterKb) / 1024.0;
            double remainingMb = afterKb / 1024.0;

            if (afterKb > 8)
            {
                return ("Partially cleared", clearedMb, remainingMb);
            }

            return ("Cleared", clearedMb, remainingMb);
        }

        private async Task<(bool success, long kb)> GetDirectorySizeKbAsync(string path)
        {
            var res = await _adbService.ExecuteCommandAsync($"shell \"du -s {path} 2>/dev/null\"");
            if (res.ExitCode == 0 && !string.IsNullOrWhiteSpace(res.Output))
            {
                var parts = res.Output.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && long.TryParse(parts[0], out long kb))
                {
                    return (true, kb);
                }
            }
            return (false, 0);
        }
    }
}