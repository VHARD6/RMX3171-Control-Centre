import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

pattern = re.compile(r'public async Task<MemoryQueryResult> GetProcessesAsync\(\).*?var meminfoResult = await _adbService\.ExecuteCommandAsync\("shell dumpsys meminfo"\);', re.DOTALL)

replacement = '''public async Task<MemoryQueryResult> GetProcessesAsync()
        {
            var result = new MemoryQueryResult
            {
                Sources = "ActivityManager + PackageManager"
            };

            var userPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -3");
            if (pmResult.ExitCode == 0)
            {
                foreach (var line in pmResult.Output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pkg = line.Replace("package:", "").Trim();
                    if (!string.IsNullOrEmpty(pkg))
                        userPackages.Add(pkg);
                }
            }
            
            var disabledPmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -d");
            if (disabledPmResult.ExitCode == 0)
            {
                foreach (var line in disabledPmResult.Output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pkg = line.Replace("package:", "").Trim();
                    if (!string.IsNullOrEmpty(pkg))
                        result.DisabledPackages.Add(pkg);
                }
            }

            var meminfoResult = await _adbService.ExecuteCommandAsync("shell dumpsys meminfo");'''

content = re.sub(pattern, replacement, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
