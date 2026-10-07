import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_methods = '''
        public async Task<List<CacheAppInfo>> GetAppCachesAsync()
        {
            var result = new List<CacheAppInfo>();
            
            // Get all packages to determine user/system
            var pm3Result = await _adbService.ExecuteCommandAsync("shell pm list packages -3");
            var userPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pm3Result.ExitCode == 0)
            {
                foreach (var line in pm3Result.Output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries))
                    userPkgs.Add(line.Replace("package:", "").Trim());
            }

            var pmSysResult = await _adbService.ExecuteCommandAsync("shell pm list packages -s");
            var sysPkgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pmSysResult.ExitCode == 0)
            {
                foreach (var line in pmSysResult.Output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries))
                    sysPkgs.Add(line.Replace("package:", "").Trim());
            }

            // Get external cache sizes
            var duResult = await _adbService.ExecuteCommandAsync("shell \\\"du -s /sdcard/Android/data/*/cache 2>/dev/null\\\"");
            var cacheSizes = new Dictionary<string, double>();
            if (duResult.ExitCode == 0)
            {
                foreach (var line in duResult.Output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { '\\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
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
                var match = System.Text.RegularExpressions.Regex.Match(res.Output, @"App Cache Size: (\\d+)");
                if (match.Success && long.TryParse(match.Groups[1].Value, out long bytes))
                {
                    return bytes / (1024.0 * 1024.0);
                }
            }
            return 0;
        }

        public async Task<(bool success, string error)> ClearAppCacheAsync(string packageName)
        {
            // 1. Clear external cache
            await _adbService.ExecuteCommandAsync($"shell \\\"rm -rf /sdcard/Android/data/{packageName}/cache/*\\\"");
            
            // 2. Try standard pm cache clearing mechanism
            var res = await _adbService.ExecuteCommandAsync($"shell pm clear --cache-only {packageName}");
            if (res.ExitCode != 0)
            {
                if (res.Output.Contains("adb clearing user data is forbidden"))
                    return (false, "OS blocked: clearing user data forbidden");
                return (false, res.Output.Trim());
            }
            return (true, "");
        }
'''

if 'GetAppCachesAsync' not in content:
    # insert before the final '}' of MemoryService
    content = content.rsplit('}', 2)
    new_content = content[0] + new_methods + '}\n}'
    with open(path, 'w', encoding='utf-8') as f:
        f.write(new_content)
