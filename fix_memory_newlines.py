import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Fix literal newlines in the Split method arguments
pattern = re.compile(r"new\[\] \{ '\n  ', '\n  ' \}", re.MULTILINE)
content = re.sub(pattern, r"new[] { '\r', '\n' }", content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
