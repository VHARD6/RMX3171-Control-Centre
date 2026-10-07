import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace interface method signature
content = content.replace('Task<(bool success, string error)> ClearAppCacheAsync(string packageName);', 'Task<string> ClearAppCacheAsync(string packageName);')

# Replace method implementation
old_method_pattern = re.compile(r'public async Task<\(bool success, string error\)> ClearAppCacheAsync\(string packageName\).*?return \(true, ""\);\s*\}', re.DOTALL)

new_method = '''public async Task<string> ClearAppCacheAsync(string packageName)
        {
            // Verify if external cache directory exists and is accessible
            var checkRes = await _adbService.ExecuteCommandAsync($"shell \\\"[ -d /sdcard/Android/data/{packageName}/cache ] && echo YES || echo NO\\\"");
            if (!checkRes.Output.Contains("YES"))
            {
                return "External cache unavailable";
            }

            // Check size to see if there's anything to clear
            var sizeRes = await _adbService.ExecuteCommandAsync($"shell \\\"du -s /sdcard/Android/data/{packageName}/cache 2>/dev/null\\\"");
            long kb = 0;
            if (sizeRes.ExitCode == 0)
            {
                var parts = sizeRes.Output.Split(new[] { '\\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && long.TryParse(parts[0], out kb))
                {
                    // Empty directories usually report as 4-8 KB.
                    if (kb <= 8) return "Nothing to clear";
                }
            }

            // Perform safe deletion of ONLY the external cache contents
            var rmRes = await _adbService.ExecuteCommandAsync($"shell \\\"rm -rf /sdcard/Android/data/{packageName}/cache/*\\\"");
            if (rmRes.ExitCode != 0)
            {
                return "Failed";
            }

            return "Cleared";
        }'''

content = old_method_pattern.sub(new_method, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
