import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add to MemoryQueryResult
old_mqr = r'''public string Sources \{ get; set; \} = "";'''
new_mqr = '''public string Sources { get; set; } = "";
        public HashSet<string> DisabledPackages { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);'''
content = re.sub(old_mqr, new_mqr, content)

# Add DisabledPackages collection logic in GetProcessesAsync
old_pm = r'''            var pmResult = await _adbService.ExecuteCommandAsync\("shell pm list packages -3"\);
            if \(pmResult.ExitCode == 0\)
            \{
                foreach \(var line in pmResult.Output.Split\(new\[\] \{ '\\r', '\\n' \}, StringSplitOptions.RemoveEmptyEntries\)\)
                \{
                    var pkg = line.Replace\("package:", ""\).Trim\(\);
                    if \(!string.IsNullOrEmpty\(pkg\)\)
                        userPackages.Add\(pkg\);
                \}
            \}'''

new_pm = '''            var pmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -3");
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
            }'''
content = re.sub(old_pm, new_pm, content)

# Add Disable/Enable commands to interface and class
old_interface = r'''        Task<bool> ForceStopAppAsync\(string packageName\);
    \}'''
new_interface = '''        Task<bool> ForceStopAppAsync(string packageName);
        Task<bool> DisablePackageAsync(string packageName);
        Task<bool> EnablePackageAsync(string packageName);
    }'''
content = re.sub(old_interface, new_interface, content)

new_methods = '''        public async Task<bool> DisablePackageAsync(string packageName)
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
'''
content = content.replace('        public async Task<bool> ForceStopAppAsync(string packageName)', new_methods + '        public async Task<bool> ForceStopAppAsync(string packageName)')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
