import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Update IsDisabled for all lists
# 1. UserAppProcesses
old_user = r'''                foreach \(var remaining in existingUsers\.Values\)
                \{
                    remaining\.RamMb = 0;
                \}'''
new_user = '''                foreach (var remaining in existingUsers.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in UserAppProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }'''
content = re.sub(old_user, new_user, content)

# 2. VendorOptionalProcesses
old_vendor = r'''                foreach \(var remaining in existingVendors\.Values\)
                \{
                    remaining\.RamMb = 0;
                \}'''
new_vendor = '''                foreach (var remaining in existingVendors.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in VendorOptionalProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }'''
content = re.sub(old_vendor, new_vendor, content)

# 3. SystemProcesses
old_sys = r'''                foreach \(var remaining in existingSys\.Values\)
                \{
                    remaining\.RamMb = 0;
                \}'''
new_sys = '''                foreach (var remaining in existingSys.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in SystemProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }'''
content = re.sub(old_sys, new_sys, content)

# 4. Also mark newly added apps in the query loops as Disabled (or wait, the loop above does it for ALL items in the collection, so we're good)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
