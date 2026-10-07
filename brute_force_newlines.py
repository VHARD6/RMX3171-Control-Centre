import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace any Split(new[] { ... }, StringSplitOptions... with the correct one
pattern = re.compile(r'Split\(new\[\] \{[^}]+\},\s*StringSplitOptions\.RemoveEmptyEntries\)', re.DOTALL)

def replacer(match):
    original = match.group(0)
    # If it's the one with ' ', ':' or ' ', '\t', keep it
    if "':'" in original or "'\\t'" in original:
        return original
    return "Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries)"

content = pattern.sub(replacer, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
