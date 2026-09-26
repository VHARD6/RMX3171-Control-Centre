using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;
        private readonly RMX3171ControlCentre.Services.Device.IDeviceService _deviceService;

        [ObservableProperty] private string _deviceInfoText = "Loading...";
        [ObservableProperty] private string _batteryText = "Loading...";
        [ObservableProperty] private string _memoryText = "Loading...";
        [ObservableProperty] private string _storageText = "Loading...";
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Live";

        public DashboardViewModel(RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService, RMX3171ControlCentre.Services.Device.IDeviceService deviceService)
        {
            _telemetryService = telemetryService;
            _deviceService = deviceService;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var snap = _telemetryService.CurrentSnapshot;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                
                dispatcher.BeginInvoke(new Action(() =>
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
                }));
            };
        }

        [ObservableProperty] private string _wirelessIp = "";
        [ObservableProperty] private string _pairingPort = "";
        [ObservableProperty] private string _pairingCode = "";
        [ObservableProperty] private string _connectionPort = "5555";
        [ObservableProperty] private string _wirelessMessage = "";
        [ObservableProperty] private string _wirelessMessageColor = "White";

        [RelayCommand]
        private async Task PairWirelessAsync()
        {
            if (string.IsNullOrWhiteSpace(WirelessIp) || string.IsNullOrWhiteSpace(PairingPort) || string.IsNullOrWhiteSpace(PairingCode))
            {
                SetWirelessMessage("IP, Port, and Code are required for pairing.", "Red");
                return;
            }

            SetWirelessMessage("Pairing...", "Orange");
            var result = await _deviceService.PairDeviceAsync(WirelessIp, PairingPort, PairingCode);
            
            if (result.Success)
            {
                SetWirelessMessage("Pairing successful! You can now connect.", "LimeGreen");
            }
            else
            {
                SetWirelessMessage($"Pairing failed: {result.Output}", "Red");
            }
        }

        [RelayCommand]
        private async Task ConnectWirelessAsync()
        {
            if (string.IsNullOrWhiteSpace(WirelessIp) || string.IsNullOrWhiteSpace(ConnectionPort))
            {
                SetWirelessMessage("IP and Port are required for connecting.", "Red");
                return;
            }

            SetWirelessMessage("Connecting...", "Orange");
            var result = await _deviceService.ConnectDeviceAsync(WirelessIp, ConnectionPort);
            
            if (result.Success && result.Output.Contains("connected"))
            {
                SetWirelessMessage("Connected successfully! You may now unplug USB.", "LimeGreen");
                await RefreshAsync();
            }
            else
            {
                SetWirelessMessage($"Connection failed: {result.Output}", "Red");
            }
        }

        [RelayCommand]
        private async Task DisconnectWirelessAsync()
        {
            SetWirelessMessage("Disconnecting...", "Orange");
            var result = await _deviceService.DisconnectDeviceAsync();
            SetWirelessMessage(result.Success ? "Disconnected all wireless devices." : "Failed to disconnect.", "White");
        }

        private void SetWirelessMessage(string message, string color)
        {
            WirelessMessage = message;
            WirelessMessageColor = color;
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
