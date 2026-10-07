import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MemoryManagerView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace CommandParameter="{Binding PackageName}" in the Action column of Disabled Apps
content = content.replace('CommandParameter="{Binding PackageName}"', 'CommandParameter="{Binding}"')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
