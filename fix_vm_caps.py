import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Disabled Apps (0)', 'DISABLED APPS (0)')
content = content.replace('Disabled Apps ({DisabledAppProcesses.Count})', 'DISABLED APPS ({DisabledAppProcesses.Count})')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
