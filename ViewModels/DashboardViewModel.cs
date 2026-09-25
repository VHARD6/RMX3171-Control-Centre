using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;

        [ObservableProperty] private string _deviceInfoText = "Loading...";
        [ObservableProperty] private string _batteryText = "Loading...";
        [ObservableProperty] private string _memoryText = "Loading...";
        [ObservableProperty] private string _storageText = "Loading...";
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Live";

        public DashboardViewModel(RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService)
        {
            _telemetryService = telemetryService;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var snap = _telemetryService.CurrentSnapshot;
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (snap.IsConnected)
                    {
                        string model = string.IsNullOrEmpty(snap.Device.Model) ? "Device" : snap.Device.Model;
                        string version = string.IsNullOrEmpty(snap.Device.AndroidVersion) ? "" : $"\nAndroid {snap.Device.AndroidVersion}";
                        DeviceInfoText = $"{model}{version}\nConnected — {snap.ConnectionTransport}";
                    }
                    else
                    {
                        DeviceInfoText = "No Device Connected";
                    }

                    if (snap.Battery.LastUpdated != System.DateTime.MinValue)
                    {
                        BatteryText = $"{snap.Battery.Percentage}%\n{snap.Battery.Status}\n{snap.Battery.Temperature:F1} °C";
                    }

                    if (snap.Memory.LastUpdated != System.DateTime.MinValue)
                    {
                        MemoryText = $"{snap.Memory.UsedRamMb / 1024.0:F1} GB / {snap.Memory.TotalRamMb / 1024.0:F1} GB";
                    }

                    if (snap.Storage.LastUpdated != System.DateTime.MinValue)
                    {
                        StorageText = $"{snap.Storage.UsedGb:F1} GB / {snap.Storage.TotalGb:F1} GB";
                    }
                    
                    var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                    LastUpdatedText = secondsAgo > 5 ? $"Last updated {secondsAgo:F1}s ago" : "Live";
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
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}
