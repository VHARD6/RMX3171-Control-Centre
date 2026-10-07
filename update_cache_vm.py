import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\CacheManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace CleanAllAsync
old_clean_all = re.compile(r'\[RelayCommand\]\s*private async Task CleanAllAsync\(\).*?finally\s*\{\s*IsCleaning = false;\s*await RefreshAsync\(\);\s*\}\s*\}', re.DOTALL)
new_clean_all = '''[RelayCommand]
        private async Task CleanAllAsync()
        {
            var eligible = CacheApps.Where(a => a.IsEligible).ToList();
            if (eligible.Count == 0)
            {
                _dialogService.ShowMessage("Clean Accessible Cache", "No eligible user apps to clean.");
                return;
            }

            double estimatedMb = eligible.Sum(a => a.CacheSizeMb);
            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Clean Accessible Cache", 
                "All Eligible User Apps", 
                "Cached", 
                "Cleared", 
                "rm -rf /sdcard/Android/data/<pkg>/cache/*", 
                "LOW", 
                $"This will remove temporary external cache files only.\\nApp accounts, settings, messages and user data will not be deleted.\\n\\nEstimated cache to process: {estimatedMb:F2} MB\\n\\nNote: Internal cache clearing is blocked by ColorOS security policy and cannot be cleared globally via ADB.");
            
            if (!confirm) return;

            IsCleaning = true;
            int cleared = 0;
            int nothing = 0;
            int unavail = 0;
            int failed = 0;
            double beforeTotalMb = TotalCacheMb;

            try
            {
                foreach (var app in eligible)
                {
                    string status = await _memoryService.ClearAppCacheAsync(app.PackageName);
                    
                    if (status == "Cleared") cleared++;
                    else if (status == "Nothing to clear") nothing++;
                    else if (status == "External cache unavailable") unavail++;
                    else failed++;
                    
                    _auditService.LogModification("Cache Clean", app.PackageName, "Before", status, "rm -rf cache", status == "Cleared" || status == "Nothing to clear");
                }

                await RefreshAsync();
                
                double afterTotalMb = TotalCacheMb;
                double actualClearedMb = Math.Max(0, beforeTotalMb - afterTotalMb);

                string summary = $"Before: {beforeTotalMb:F2} MB\n" +
                                 $"Successfully cleared: {actualClearedMb:F2} MB\n" +
                                 $"Remaining detected cache: {afterTotalMb:F2} MB\n\n" +
                                 $"Results:\n" +
                                 $"- Cleared: {cleared}\n" +
                                 $"- Nothing to clear: {nothing}\n" +
                                 $"- External cache unavailable: {unavail}\n" +
                                 $"- Failed/skipped: {failed}\n\n" +
                                 $"(Internal caches are blocked by ColorOS security policy and were skipped)";

                _dialogService.ShowMessage("Cache Clean Complete", summary);
            }
            finally
            {
                IsCleaning = false;
            }
        }'''

content = old_clean_all.sub(new_clean_all, content)

# Replace ClearSingleAsync
old_clear_single = re.compile(r'\[RelayCommand\]\s*private async Task ClearSingleAsync\(CacheAppInfo app\).*?finally\s*\{\s*IsCleaning = false;\s*\}\s*\}', re.DOTALL)
new_clear_single = '''[RelayCommand]
        private async Task ClearSingleAsync(CacheAppInfo app)
        {
            if (app == null) return;
            if (!app.IsEligible)
            {
                _dialogService.ShowMessage("Restricted", $"Cannot clear cache for {app.PackageName}.\\nReason: {app.ExcludeReason}");
                return;
            }

            IsCleaning = true;
            try
            {
                string status = await _memoryService.ClearAppCacheAsync(app.PackageName);
                _auditService.LogModification("Cache Clean", app.PackageName, "Before", status, "rm -rf cache", status == "Cleared" || status == "Nothing to clear");
                
                if (status == "Cleared")
                {
                    await RefreshAsync();
                }
                else
                {
                    _dialogService.ShowMessage("Result", $"Status for {app.PackageName}:\\n{status}\\n\\n(Note: Internal cache clearing is blocked by ColorOS)");
                }
            }
            finally
            {
                IsCleaning = false;
            }
        }'''

content = old_clear_single.sub(new_clear_single, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
