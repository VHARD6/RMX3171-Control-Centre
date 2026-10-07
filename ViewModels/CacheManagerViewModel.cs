using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class CacheManagerViewModel : ViewModelBase
    {
        private readonly IMemoryService _memoryService;
        private readonly IDialogService _dialogService;
        private readonly IAuditService _auditService;

        [ObservableProperty]
        private ObservableCollection<CacheAppInfo> _cacheApps = new ObservableCollection<CacheAppInfo>();

        [ObservableProperty]
        private double _totalCacheMb;
        
        [ObservableProperty]
        private string _totalCacheLabel = "0 MB";

        [ObservableProperty]
        private string _appsWithCacheLabel = "0 Apps";

        [ObservableProperty]
        private bool _isRefreshing;
        
        [ObservableProperty]
        private bool _isCleaning;

        public CacheManagerViewModel(IMemoryService memoryService, IDialogService dialogService, IAuditService auditService)
        {
            _memoryService = memoryService;
            _dialogService = dialogService;
            _auditService = auditService;
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                TotalCacheMb = await _memoryService.GetTotalCacheSizeMbAsync();
                TotalCacheLabel = $"{TotalCacheMb:F2} MB";

                var apps = await _memoryService.GetAppCachesAsync();
                CacheApps.Clear();
                foreach (var a in apps) CacheApps.Add(a);

                AppsWithCacheLabel = $"{CacheApps.Count} Apps";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task CleanAllAsync()
        {
            var eligible = CacheApps.Where(a => a.IsEligible).ToList();
            if (eligible.Count == 0)
            {
                _dialogService.ShowMessage("Clean Accessible Cache", "No eligible user apps to clean.");
                return;
            }

            double estimatedMb = eligible.Sum(a => a.CacheSizeMb);
            string msg = "This will remove temporary external cache files only.\nApp accounts, settings, messages and user data will not be deleted.\n\nEstimated cache to process: " + estimatedMb.ToString("F2") + " MB\n\nNote: Internal cache clearing is blocked by ColorOS security policy and cannot be cleared globally via ADB.";
            
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
            int measurementFailed = 0;
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
                    else if (result.status == "Measurement failed") measurementFailed++;
                    else failed++;
                    
                    actualClearedMb += result.clearedMb;
                    actualRemainingMb += result.remainingMb;
                    
                    bool isSuccess = result.status == "Cleared" || result.status == "Nothing to clear" || result.status == "Partially cleared";
                    _auditService.LogModification("Cache Clean", app.PackageName, "Before", result.status, "rm -rf cache", isSuccess);
                }

                await RefreshAsync();

                string summary = "Packages successfully processed: " + (cleared + nothing + partial) + "\n" +
                                 "Measured cache reduction: " + actualClearedMb.ToString("F2") + " MB\n" +
                                 "Remaining measured external cache: " + actualRemainingMb.ToString("F2") + " MB\n\n" +
                                 "Results:\n" +
                                 "- Cleared: " + cleared + "\n" +
                                 "- Partially cleared: " + partial + "\n" +
                                 "- Nothing to clear: " + nothing + "\n" +
                                 "- External cache unavailable: " + unavail + "\n" +
                                 "- Measurement failed: " + measurementFailed + "\n" +
                                 "- Failed: " + failed + "\n\n" +
                                 "(Device-wide cache shown on dashboard is global and informational)";

                _dialogService.ShowMessage("Cache Clean Complete", summary);
            }
            finally
            {
                IsCleaning = false;
            }
        }

        [RelayCommand]
        private async Task ClearSingleAsync(CacheAppInfo app)
        {
            if (app == null) return;
            if (!app.IsEligible)
            {
                _dialogService.ShowMessage("Restricted", "Cannot clear cache for " + app.PackageName + ".\nReason: " + app.ExcludeReason);
                return;
            }

            IsCleaning = true;
            try
            {
                var result = await _memoryService.ClearAppCacheAsync(app.PackageName);
                bool isSuccess = result.status == "Cleared" || result.status == "Nothing to clear" || result.status == "Partially cleared";
                _auditService.LogModification("Cache Clean", app.PackageName, "Before", result.status, "rm -rf cache", isSuccess);
                
                if (result.status == "Cleared" || result.status == "Partially cleared" || result.status == "Nothing to clear")
                {
                    await RefreshAsync();
                    _dialogService.ShowMessage("Result", "Status for " + app.PackageName + ":\n" + result.status + "\nMeasured cache reduction: " + result.clearedMb.ToString("F2") + " MB");
                }
                else
                {
                    _dialogService.ShowMessage("Result", "Status for " + app.PackageName + ":\n" + result.status + "\n\n(Note: Internal cache clearing is blocked by ColorOS)");
                }
            }
            finally
            {
                IsCleaning = false;
            }
        }

        [RelayCommand]
        private async Task InstallHelperAsync()
        {
            _dialogService.ShowMessage("Install Phone Cache Cleaner", 
                "Because the Android SDK / workloads are not installed on this system, the helper APK cannot be compiled and installed automatically.\n\nThe source code for the Android helper app has been generated in the 'AndroidHelper' directory. Please compile it using Android Studio and install it manually.\n\nNote: Android 11 OS restricts global cache cleaning for non-system apps. The helper will provide a shortcut to the system Storage Settings.");
        }
    }
}