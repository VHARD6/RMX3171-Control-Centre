import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add DisabledAppProcesses property
pattern = r'private ObservableCollection<AppProcessInfo> _systemProcesses = new ObservableCollection<AppProcessInfo>\(\);'
replacement = r'''private ObservableCollection<AppProcessInfo> _systemProcesses = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _disabledAppProcesses = new ObservableCollection<AppProcessInfo>();
        
        [ObservableProperty]
        private string _disabledCountLabel = "Disabled Apps (0)";'''
content = re.sub(pattern, replacement, content)

# Add Merge logic in RefreshAsync
merge_pattern = r'foreach \(var app in SystemProcesses\)\s*\{\s*app\.IsDisabled = query\.DisabledPackages\.Contains\(app\.PackageName\);\s*\}'
merge_replacement = r'''foreach (var app in SystemProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }

                // Merge DisabledAppProcesses
                var existingDisabled = DisabledAppProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.DisabledAppProcesses)
                {
                    if (existingDisabled.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.IsDisabled = true;
                        existing.DisabledStateDetail = proc.DisabledStateDetail;
                        existingDisabled.Remove(proc.PackageName);
                    }
                    else
                    {
                        DisabledAppProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingDisabled.Values)
                {
                    DisabledAppProcesses.Remove(remaining);
                }
                
                DisabledCountLabel = $"Disabled Apps ({DisabledAppProcesses.Count})";'''
content = re.sub(merge_pattern, merge_replacement, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
