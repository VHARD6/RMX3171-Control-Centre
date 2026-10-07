import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add DisabledAppProcesses to MemoryQueryResult
pattern = re.compile(r'public HashSet<string> DisabledPackages \{ get; set; \} = new HashSet<string>\(StringComparer\.OrdinalIgnoreCase\);')
replacement = r'''public HashSet<string> DisabledPackages { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<AppProcessInfo> DisabledAppProcesses { get; set; } = new List<AppProcessInfo>();'''
content = pattern.sub(replacement, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
