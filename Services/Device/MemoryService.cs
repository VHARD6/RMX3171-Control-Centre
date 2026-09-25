using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.Services.Device
{
    public interface IMemoryService
    {
        Task<MemoryInfo> GetMemoryInfoAsync();
        Task<List<AppProcessInfo>> GetTopConsumersAsync();
        Task<bool> ForceStopAppAsync(string packageName);
    }

    public class MemoryService : IMemoryService
    {
        private readonly IAdbService _adbService;
        private readonly ILogService _logService;

        private static readonly HashSet<string> ProtectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.android.systemui",
            "com.android.phone",
            "com.android.settings",
            "com.google.android.gms",
            "android",
            "com.coloros.safecenter",
            "com.oppo.launcher",
            "com.android.launcher3",
            "com.realme.launcher",
            "com.coloros.launcher",
            "com.heytap.mcs",
            "com.google.android.gsf",
            "com.google.android.webview",
            "com.android.vending",
            "com.oplus.battery",
            "com.oplus.safecenter"
        };

        public MemoryService(IAdbService adbService, ILogService logService)
        {
            _adbService = adbService;
            _logService = logService;
        }

        public async Task<MemoryInfo> GetMemoryInfoAsync()
        {
            var memInfo = new MemoryInfo();

            // /proc/meminfo
            var memInfoResult = await _adbService.ExecuteCommandAsync("shell cat /proc/meminfo");
            if (memInfoResult.ExitCode == 0)
            {
                var lines = memInfoResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && double.TryParse(parts[1], out double kb))
                    {
                        double gb = kb / (1024.0 * 1024.0);
                        switch (parts[0])
                        {
                            case "MemTotal": memInfo.TotalGB = gb; break;
                            case "MemFree": memInfo.FreeGB = gb; break;
                            case "MemAvailable": memInfo.AvailableGB = gb; break;
                            case "Cached": memInfo.CachedGB = gb; break;
                        }
                    }
                }
                memInfo.UsedGB = Math.Max(0, memInfo.TotalGB - memInfo.AvailableGB);
            }

            // /proc/swaps (ZRAM)
            var swapResult = await _adbService.ExecuteCommandAsync("shell cat /proc/swaps");
            if (swapResult.ExitCode == 0)
            {
                var lines = swapResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("/dev/block/zram0"))
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

            if (memInfo.TotalGB > 0)
            {
                var ratio = memInfo.AvailableGB / memInfo.TotalGB;
                if (ratio < 0.1) memInfo.MemoryPressure = "Critical";
                else if (ratio < 0.2) memInfo.MemoryPressure = "High";
                else if (ratio < 0.4) memInfo.MemoryPressure = "Moderate";
                else memInfo.MemoryPressure = "Normal";
            }

            return memInfo;
        }

        public async Task<List<AppProcessInfo>> GetTopConsumersAsync()
        {
            var apps = new List<AppProcessInfo>();

            var result = await _adbService.ExecuteCommandAsync("shell dumpsys meminfo");
            if (result.ExitCode != 0) return apps;

            bool inPssSection = false;
            var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            Regex processRegex = new Regex(@"^\s*([\d,]+)\s+K:\s+([a-zA-Z0-9\._:]+)\s*\(pid\s+\d+\)");

            foreach (var line in lines)
            {
                if (line.Contains("Total PSS by process:"))
                {
                    inPssSection = true;
                    continue;
                }
                if (inPssSection && (line.Contains("Total PSS by OOM adjustment:") || line.Contains("Total PSS by category:")))
                {
                    break;
                }

                if (inPssSection)
                {
                    var match = processRegex.Match(line);
                    if (match.Success)
                    {
                        string kPssStr = match.Groups[1].Value.Replace(",", "");
                        string processName = match.Groups[2].Value;
                        
                        if (double.TryParse(kPssStr, out double pssKb))
                        {
                            double mb = pssKb / 1024.0;
                            if (mb < 10) continue; 

                            string packageName = processName.Contains(":") ? processName.Substring(0, processName.IndexOf(':')) : processName;
                            
                            var existing = apps.FirstOrDefault(a => a.PackageName == packageName);
                            if (existing != null)
                            {
                                existing.RamMb += mb;
                            }
                            else
                            {
                                bool isProtected = ProtectedPackages.Contains(packageName) || 
                                                   packageName.StartsWith("com.android.") ||
                                                   packageName.StartsWith("com.coloros.") ||
                                                   packageName.StartsWith("com.oplus.") ||
                                                   packageName.StartsWith("com.realme.");

                                apps.Add(new AppProcessInfo
                                {
                                    PackageName = packageName,
                                    AppName = packageName,
                                    RamMb = mb,
                                    Importance = "Unknown",
                                    IsProtected = isProtected
                                });
                            }
                        }
                    }
                }
            }

            return apps.OrderByDescending(a => a.RamMb).ToList();
        }

        public async Task<bool> ForceStopAppAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            _logService.LogDebug($"User requested force-stop for package: {packageName}");
            
            var result = await _adbService.ExecuteCommandAsync($"shell am force-stop {packageName}", isReadOnly: false);
            
            _logService.LogDebug($"Result of force-stop {packageName}: ExitCode={result.ExitCode}");
            
            return result.ExitCode == 0;
        }
    }
}
