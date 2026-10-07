import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace("new[] { '\n', '\n' }", "new[] { '\\r', '\\n' }")

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
