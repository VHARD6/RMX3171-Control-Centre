import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\Storage\CleanupOpportunitiesViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

import re

# Fix adbCmd for preview
old_adbcmd = r'''string adbCmd = selected.Count == 1 
                \? \$"shell rm \\"\{firstItem.FullPath\}\\"" 
                : \$"shell rm \.\.\. \(\{selected.Count\} items\)";'''

new_adbcmd = '''string escapedFirstPath = firstItem.FullPath.Replace("'", "'\\\\''");
            string adbCmd = selected.Count == 1 
                ? $"shell rm '{escapedFirstPath}'" 
                : $"shell rm ... ({selected.Count} items)";'''

content = re.sub(old_adbcmd, new_adbcmd, content)

# Fix cmd inside the loop
old_cmd = r'''var cmd = \$"shell rm \\"\{item.FullPath\}\\"";'''
new_cmd = '''string escapedItemPath = item.FullPath.Replace("'", "'\\\\''");
                    var cmd = $"shell rm '{escapedItemPath}'";'''

content = re.sub(old_cmd, new_cmd, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
