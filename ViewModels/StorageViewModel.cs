using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class StorageViewModel : ViewModelBase
    {
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;

        [ObservableProperty] private string _storageRawInfo = "Managed by background telemetry.";
        [ObservableProperty] private string _internalTotal = "Loading...";
        [ObservableProperty] private string _internalUsed = "Loading...";
        [ObservableProperty] private string _internalFree = "Loading...";
        [ObservableProperty] private int _internalUsagePercent = 0;
        [ObservableProperty] private string _internalUsageText = "0%";
        [ObservableProperty] private bool _isAdvancedVisible = false;
        
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Waiting for data...";

        public StorageViewModel(RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService)
        {
            _telemetryService = telemetryService;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var snap = _telemetryService.CurrentSnapshot.Storage;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (snap.LastUpdated != System.DateTime.MinValue)
                    {
                        InternalTotal = $"{snap.TotalGb:F1} GB";
                        InternalUsed = $"{snap.UsedGb:F1} GB";
                        InternalFree = $"{snap.FreeGb:F1} GB";
                        if (snap.TotalGb > 0)
                        {
                            int pct = (int)((snap.UsedGb / snap.TotalGb) * 100);
                            InternalUsagePercent = pct;
                            InternalUsageText = $"{pct}%";
                        }
                        
                        var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                        LastUpdatedText = secondsAgo > 20 ? $"Last updated {secondsAgo:F1}s ago" : "Live";
                    }
                });
            };
        }

        [RelayCommand]
        private void ToggleAdvanced() => IsAdvancedVisible = !IsAdvancedVisible;

        [RelayCommand]
        private async Task RefreshStorageAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                await _telemetryService.ForceRefreshAsync("All");
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}
