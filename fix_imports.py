import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\CacheManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('using RMX3171ControlCentre.Services.Device;', 'using RMX3171ControlCentre.Services.Device;\nusing RMX3171ControlCentre.Services.Security;')
content = content.replace('private readonly MemoryService _memoryService;', 'private readonly IMemoryService _memoryService;')
content = content.replace('public CacheManagerViewModel(MemoryService memoryService', 'public CacheManagerViewModel(IMemoryService memoryService')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
