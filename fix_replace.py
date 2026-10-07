import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\Storage\CleanupOpportunitiesViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# We want the literal text in the C# file to be:
# Replace("'", "'\\''");

content = content.replace('Replace("\'", "\'\\\'\'")', 'Replace("\'", "\'\\\\\'\'")')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
