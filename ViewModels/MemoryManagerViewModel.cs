using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class MemoryManagerViewModel : ViewModelBase
    {
        private readonly IMemoryService _memoryService;
        private readonly IDialogService _dialogService;
        private readonly IAppModeService _appModeService;
        private readonly IAuditService _auditService;
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;

        [ObservableProperty]
        private MemoryInfo _currentMemoryInfo = new MemoryInfo();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _topConsumers = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private bool _isRefreshing;
        
        [ObservableProperty]
        private string _lastUpdatedText = "Waiting for data...";

        public MemoryManagerViewModel(
            IMemoryService memoryService,
            IDialogService dialogService,
            IAppModeService appModeService,
            IAuditService auditService,
            RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService)
        {
            _memoryService = memoryService;
            _dialogService = dialogService;
            _appModeService = appModeService;
            _auditService = auditService;
            _telemetryService = telemetryService;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var snap = _telemetryService.CurrentSnapshot.Memory;
                    if (snap.LastUpdated != System.DateTime.MinValue)
                    {
                        CurrentMemoryInfo.TotalGB = snap.TotalRamMb / 1024.0;
                        CurrentMemoryInfo.UsedGB = snap.UsedRamMb / 1024.0;
                        CurrentMemoryInfo.AvailableGB = snap.AvailableRamMb / 1024.0;
                        CurrentMemoryInfo.CachedGB = snap.CachedRamMb / 1024.0;
                        CurrentMemoryInfo.ZramTotalGB = snap.ZramTotalMb / 1024.0;
                        CurrentMemoryInfo.ZramUsedGB = snap.ZramUsedMb / 1024.0;
                        CurrentMemoryInfo.FreeGB = (snap.AvailableRamMb - snap.CachedRamMb) / 1024.0;
                        
                        var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                        LastUpdatedText = secondsAgo > 5 ? $"Last updated {secondsAgo:F1}s ago" : "Live";

                        OnPropertyChanged(nameof(CurrentMemoryInfo));
                    }
                });
            };
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                await _telemetryService.ForceRefreshAsync("All");
                
                var consumers = await _memoryService.GetTopConsumersAsync();
                TopConsumers.Clear();
                foreach (var c in consumers)
                {
                    TopConsumers.Add(c);
                }
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task ForceStopAsync(AppProcessInfo app)
        {
            if (app == null || app.IsProtected || IsRefreshing) return;

            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Force Stop is a MODIFY operation.\nPlease enter Advanced Mode to perform this action.");
                return;
            }

            string adbCommand = $"adb shell am force-stop {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Force Stop Application",
                app.PackageName,
                "Running in background/foreground",
                "Force Stopped",
                adbCommand,
                "MODIFY",
                "This will forcefully terminate the application and its background services. Unsaved data in the app may be lost."
            );
            
            if (confirm)
            {
                bool success = await _memoryService.ForceStopAppAsync(app.PackageName);
                _auditService.LogModification("Force Stop", app.PackageName, "Running", "Stopped", adbCommand, success);

                if (success)
                {
                    _dialogService.ShowMessage("Success", $"Successfully closed {app.PackageName}. RAM released.");
                    await RefreshAsync();
                }
                else
                {
                    _dialogService.ShowMessage("Error", $"Failed to force stop {app.PackageName}.");
                }
            }
        }
    }
}
