import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\App.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

if 'CacheManagerViewModel' not in content:
    content = content.replace('services.AddSingleton<MemoryManagerViewModel>();', 'services.AddSingleton<MemoryManagerViewModel>();\n            services.AddSingleton<CacheManagerViewModel>();')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
