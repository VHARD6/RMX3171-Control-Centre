import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\CacheManagerView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Content="CLEAN ALL APP CACHE"', 'Content="CLEAN ACCESSIBLE CACHE"')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
