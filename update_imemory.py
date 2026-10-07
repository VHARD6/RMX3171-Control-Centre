import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_methods = '''
        Task<System.Collections.Generic.List<RMX3171ControlCentre.Models.CacheAppInfo>> GetAppCachesAsync();
        Task<double> GetTotalCacheSizeMbAsync();
        Task<(bool success, string error)> ClearAppCacheAsync(string packageName);
'''

if 'GetAppCachesAsync()' not in content:
    content = content.replace('Task<bool> EnablePackageAsync(string packageName);', 'Task<bool> EnablePackageAsync(string packageName);\n' + new_methods)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
