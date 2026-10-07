import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

import re

# Fix VendorOptional
old_vendor = r'''                foreach \(var proc in query\.VendorOptional\)
                                        proc\.PropertyChanged \+= \(s, e\) => \{ if \(e\.PropertyName == nameof\(AppProcessInfo\.IsSelected\)\) UpdateEstimatedReclaimable\(\); \};
                    VendorOptionalProcesses\.Add\(proc\);'''

new_vendor = '''                foreach (var proc in query.VendorOptional)
                {
                    proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                    VendorOptionalProcesses.Add(proc);
                }'''
content = re.sub(old_vendor, new_vendor, content)

# Fix SystemProcesses
old_system = r'''                foreach \(var proc in query\.SystemProcesses\)
                                        proc\.PropertyChanged \+= \(s, e\) => \{ if \(e\.PropertyName == nameof\(AppProcessInfo\.IsSelected\)\) UpdateEstimatedReclaimable\(\); \};
                    SystemProcesses\.Add\(proc\);'''

new_system = '''                foreach (var proc in query.SystemProcesses)
                {
                    proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                    SystemProcesses.Add(proc);
                }'''
content = re.sub(old_system, new_system, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
