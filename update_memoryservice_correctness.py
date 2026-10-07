import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Update Interface
content = content.replace('Task<string> ClearAppCacheAsync(string packageName);', 'Task<(string status, double clearedMb, double remainingMb)> ClearAppCacheAsync(string packageName);')

# Replace Method Implementation
old_method_pattern = re.compile(r'public async Task<string> ClearAppCacheAsync\(string packageName\).*?return "Cleared";\s*\}', re.DOTALL)

new_method = '''public async Task<(string status, double clearedMb, double remainingMb)> ClearAppCacheAsync(string packageName)
        {
            var checkRes = await _adbService.ExecuteCommandAsync($"shell \\\"[ -d /sdcard/Android/data/{packageName}/cache ] && echo YES || echo NO\\\"");
            if (!checkRes.Output.Contains("YES"))
            {
                return ("External cache unavailable", 0, 0);
            }

            long beforeKb = await GetDirectorySizeKb($"/sdcard/Android/data/{packageName}/cache");
            if (beforeKb <= 8)
            {
                return ("Nothing to clear", 0, beforeKb / 1024.0);
            }

            var rmRes = await _adbService.ExecuteCommandAsync($"shell \\\"rm -rf /sdcard/Android/data/{packageName}/cache/*\\\"");
            if (rmRes.ExitCode != 0)
            {
                return ("Failed", 0, beforeKb / 1024.0);
            }

            long afterKb = await GetDirectorySizeKb($"/sdcard/Android/data/{packageName}/cache");
            double clearedMb = Math.Max(0, beforeKb - afterKb) / 1024.0;
            double remainingMb = afterKb / 1024.0;

            if (afterKb > 8)
            {
                return ("Partially cleared", clearedMb, remainingMb);
            }

            return ("Cleared", clearedMb, remainingMb);
        }

        private async Task<long> GetDirectorySizeKb(string path)
        {
            var res = await _adbService.ExecuteCommandAsync($"shell \\\"du -s {path} 2>/dev/null\\\"");
            if (res.ExitCode == 0)
            {
                var parts = res.Output.Split(new[] { '\\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && long.TryParse(parts[0], out long kb))
                {
                    return kb;
                }
            }
            return 0;
        }'''

content = old_method_pattern.sub(new_method, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
