import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

pattern = re.compile(r'var disabledPmResult = await _adbService\.ExecuteCommandAsync\("shell pm list packages -d"\);\s*if \(disabledPmResult\.ExitCode == 0\)\s*\{\s*foreach \(var line in disabledPmResult\.Output\.Split\(new\[\] \{ \'\\r\', \'\\n\' \}, StringSplitOptions\.RemoveEmptyEntries\)\)\s*\{\s*var pkg = line\.Replace\("package:", ""\)\.Trim\(\);\s*if \(!string\.IsNullOrEmpty\(pkg\)\)\s*result\.DisabledPackages\.Add\(pkg\);\s*\}\s*\}')

replacement = r'''var disabledPmResult = await _adbService.ExecuteCommandAsync("shell pm list packages -d --user 0");
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
            }'''

content = pattern.sub(replacement, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
