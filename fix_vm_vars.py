import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\CacheManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('"} MB";', '$\"{TotalCacheMb:F2} MB\";')
content = content.replace('".Count} Apps";', '$\"{CacheApps.Count} Apps\";')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
