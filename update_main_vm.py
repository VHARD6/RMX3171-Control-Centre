import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MainViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

if 'CacheManagerViewModel' not in content:
    content = content.replace('public AppManagerViewModel AppManagerVM { get; }', 'public AppManagerViewModel AppManagerVM { get; }\n        public CacheManagerViewModel CacheManagerVM { get; }')
    content = content.replace('MemoryManagerViewModel memoryManagerVM,', 'MemoryManagerViewModel memoryManagerVM,\n            CacheManagerViewModel cacheManagerVM,')
    content = content.replace('MemoryManagerVM = memoryManagerVM;', 'MemoryManagerVM = memoryManagerVM;\n            CacheManagerVM = cacheManagerVM;')
    content = content.replace('\"Memory\" => MemoryManagerVM,', '\"Memory\" => MemoryManagerVM,\n                \"Cache\" => CacheManagerVM,')

    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
