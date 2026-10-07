import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Merge UserAppProcesses
old_user = r'''                UserAppProcesses.Clear\(\);
                foreach \(var app in query.UserApps\)
                \{
                    app.IsExcludedFromQuickClean = exclusions.Contains\(app.PackageName\);
                    app.PropertyChanged \+= async \(s, e\) =>
                    \{
                                                if \(e.PropertyName == nameof\(AppProcessInfo.IsSelected\)\)
                        \{
                            UpdateEstimatedReclaimable\(\);
                        \}
                        if \(e.PropertyName == nameof\(AppProcessInfo.IsExcludedFromQuickClean\)\)
                        \{
                            var current = await _configService.GetExcludedPackagesAsync\(\);
                            if \(app.IsExcludedFromQuickClean\) current.Add\(app.PackageName\);
                            else current.Remove\(app.PackageName\);
                            await _configService.SaveExcludedPackagesAsync\(current\);
                        \}
                    \};
                    UserAppProcesses.Add\(app\);
                \}'''

new_user = '''                // Merge UserAppProcesses
                var existingUsers = UserAppProcesses.ToDictionary(a => a.PackageName);
                foreach (var app in query.UserApps)
                {
                    if (existingUsers.TryGetValue(app.PackageName, out var existing))
                    {
                        existing.RamMb = app.RamMb;
                        existingUsers.Remove(app.PackageName);
                    }
                    else
                    {
                        app.IsExcludedFromQuickClean = exclusions.Contains(app.PackageName);
                        app.PropertyChanged += async (s, e) =>
                        {
                            if (e.PropertyName == nameof(AppProcessInfo.IsSelected))
                            {
                                UpdateEstimatedReclaimable();
                            }
                            if (e.PropertyName == nameof(AppProcessInfo.IsExcludedFromQuickClean))
                            {
                                var current = await _configService.GetExcludedPackagesAsync();
                                if (app.IsExcludedFromQuickClean) current.Add(app.PackageName);
                                else current.Remove(app.PackageName);
                                await _configService.SaveExcludedPackagesAsync(current);
                            }
                        };
                        UserAppProcesses.Add(app);
                    }
                }
                foreach (var remaining in existingUsers.Values)
                {
                    remaining.RamMb = 0;
                }'''
content = re.sub(old_user, new_user, content)


# Merge VendorOptionalProcesses
old_vendor = r'''                VendorOptionalProcesses.Clear\(\);
                foreach \(var proc in query.VendorOptional\)
                \{
                    proc.PropertyChanged \+= \(s, e\) => \{ if \(e.PropertyName == nameof\(AppProcessInfo.IsSelected\)\) UpdateEstimatedReclaimable\(\); \};
                    VendorOptionalProcesses.Add\(proc\);
                \}'''

new_vendor = '''                // Merge VendorOptionalProcesses
                var existingVendors = VendorOptionalProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.VendorOptional)
                {
                    if (existingVendors.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.RamMb = proc.RamMb;
                        existingVendors.Remove(proc.PackageName);
                    }
                    else
                    {
                        proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                        VendorOptionalProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingVendors.Values)
                {
                    remaining.RamMb = 0;
                }'''
content = re.sub(old_vendor, new_vendor, content)

# Merge SystemProcesses
old_sys = r'''                SystemProcesses.Clear\(\);
                foreach \(var proc in query.SystemProcesses\)
                \{
                    proc.PropertyChanged \+= \(s, e\) => \{ if \(e.PropertyName == nameof\(AppProcessInfo.IsSelected\)\) UpdateEstimatedReclaimable\(\); \};
                    SystemProcesses.Add\(proc\);
                \}'''

new_sys = '''                // Merge SystemProcesses
                var existingSys = SystemProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.SystemProcesses)
                {
                    if (existingSys.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.RamMb = proc.RamMb;
                        existingSys.Remove(proc.PackageName);
                    }
                    else
                    {
                        proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                        SystemProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingSys.Values)
                {
                    remaining.RamMb = 0;
                }'''
content = re.sub(old_sys, new_sys, content)


with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
