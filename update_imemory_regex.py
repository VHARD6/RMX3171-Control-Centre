import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Device\MemoryService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

pattern = re.compile(r'Task<bool> EnablePackageAsync\(string packageName\);')
new_methods = '''Task<bool> EnablePackageAsync(string packageName);
        Task<System.Collections.Generic.List<RMX3171ControlCentre.Models.CacheAppInfo>> GetAppCachesAsync();
        Task<double> GetTotalCacheSizeMbAsync();
        Task<(bool success, string error)> ClearAppCacheAsync(string packageName);'''

content = pattern.sub(new_methods, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
