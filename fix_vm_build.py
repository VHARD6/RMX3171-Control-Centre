import sys

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

path_vm = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\CacheManagerViewModel.cs'
with open(path_vm, 'r', encoding='utf-8') as f:
    content_vm = f.read()

content_vm = content_vm.replace('public partial class CacheManagerViewModel : ObservableObject', 'public partial class CacheManagerViewModel : ViewModelBase')

old_confirm = '''bool confirm = _dialogService.ShowConfirmation("Clear application caches?", 
                $"This will remove temporary cached files only.\\nApp accounts, settings, messages and user data will not be deleted.\\n\\nEstimated cache: {estimatedMb:F2} MB");'''

new_confirm = '''bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Clean All Cache", 
                "All Eligible User Apps", 
                "Cached", 
                "Cleared", 
                "pm clear --cache-only <pkg> (via ADB)", 
                "LOW", 
                $"This will remove temporary cached files only.\\nApp accounts, settings, messages and user data will not be deleted.\\n\\nEstimated cache to process: {estimatedMb:F2} MB");'''

content_vm = content_vm.replace(old_confirm, new_confirm)

with open(path_vm, 'w', encoding='utf-8') as f:
    f.write(content_vm)
