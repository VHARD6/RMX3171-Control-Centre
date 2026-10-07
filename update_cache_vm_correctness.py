import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\CacheManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace CleanAllAsync
old_clean_all = re.compile(r'\[RelayCommand\]\s*private async Task CleanAllAsync\(\).*?finally\s*\{\s*IsCleaning = false;\s*\}\s*\}', re.DOTALL)
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
            string msg = "This will remove temporary external cache files only.\\nApp accounts, settings, messages and user data will not be deleted.\\n\\nEstimated cache to process: " + estimatedMb.ToString("F2") + " MB\\n\\nNote: Internal cache clearing is blocked by ColorOS security policy and cannot be cleared globally via ADB.";
            
            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Clean Accessible Cache", 
                "All Eligible User Apps", 
                "Cached", 
                "Cleared", 
                "rm -rf /sdcard/Android/data/<pkg>/cache/*", 
                "LOW", 
                msg);
            
            if (!confirm) return;

            IsCleaning = true;
            int cleared = 0;
            int nothing = 0;
            int unavail = 0;
            int failed = 0;
            int partial = 0;
            double actualClearedMb = 0;
            double actualRemainingMb = 0;

            try
            {
                foreach (var app in eligible)
                {
                    var result = await _memoryService.ClearAppCacheAsync(app.PackageName);
                    
                    if (result.status == "Cleared") cleared++;
                    else if (result.status == "Nothing to clear") nothing++;
                    else if (result.status == "External cache unavailable") unavail++;
                    else if (result.status == "Partially cleared") partial++;
                    else failed++;
                    
                    actualClearedMb += result.clearedMb;
                    actualRemainingMb += result.remainingMb;
                    
                    _auditService.LogModification("Cache Clean", app.PackageName, "Before", result.status, "rm -rf cache", result.status == "Cleared" || result.status == "Nothing to clear" || result.status == "Partially cleared");
                }

                await RefreshAsync();

                string summary = "Packages successfully processed: " + (cleared + nothing + partial) + "\\n" +
                                 "Bytes actually removed: " + actualClearedMb.ToString("F2") + " MB\\n" +
                                 "Remaining external cache (processed apps): " + actualRemainingMb.ToString("F2") + " MB\\n\\n" +
                                 "Results:\\n" +
                                 "- Cleared: " + cleared + "\\n" +
                                 "- Partially cleared: " + partial + "\\n" +
                                 "- Nothing to clear: " + nothing + "\\n" +
                                 "- External cache unavailable: " + unavail + "\\n" +
                                 "- Failed: " + failed + "\\n\\n" +
                                 "(Device-wide cache shown on dashboard is global and informational)";

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
                _dialogService.ShowMessage("Restricted", "Cannot clear cache for " + app.PackageName + ".\\nReason: " + app.ExcludeReason);
                return;
            }

            IsCleaning = true;
            try
            {
                var result = await _memoryService.ClearAppCacheAsync(app.PackageName);
                _auditService.LogModification("Cache Clean", app.PackageName, "Before", result.status, "rm -rf cache", result.status == "Cleared" || result.status == "Nothing to clear" || result.status == "Partially cleared");
                
                if (result.status == "Cleared" || result.status == "Partially cleared" || result.status == "Nothing to clear")
                {
                    await RefreshAsync();
                    _dialogService.ShowMessage("Result", "Status for " + app.PackageName + ":\\n" + result.status + "\\nCleared: " + result.clearedMb.ToString("F2") + " MB");
                }
                else
                {
                    _dialogService.ShowMessage("Result", "Status for " + app.PackageName + ":\\n" + result.status + "\\n\\n(Note: Internal cache clearing is blocked by ColorOS)");
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
