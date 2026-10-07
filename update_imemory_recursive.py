import os

target = 'Task<bool> EnablePackageAsync(string packageName);'
new_methods = '''Task<bool> EnablePackageAsync(string packageName);
        Task<System.Collections.Generic.List<RMX3171ControlCentre.Models.CacheAppInfo>> GetAppCachesAsync();
        Task<double> GetTotalCacheSizeMbAsync();
        Task<(bool success, string error)> ClearAppCacheAsync(string packageName);'''

for root, dirs, files in os.walk(r'C:\Users\omras\RMX3171ControlCentre_V2\Services'):
    for file in files:
        if file.endswith('.cs'):
            path = os.path.join(root, file)
            with open(path, 'r', encoding='utf-8') as f:
                content = f.read()
            if target in content and 'GetAppCachesAsync' not in content:
                content = content.replace(target, new_methods)
                with open(path, 'w', encoding='utf-8') as f:
                    f.write(content)
                print('Updated', path)
