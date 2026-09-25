using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.Services.Adb;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly IDeviceService _deviceService;
        private readonly IAdbService _adbService;

        [ObservableProperty] private string _model = "Loading...";
        [ObservableProperty] private string _androidVersion = "Loading...";
        [ObservableProperty] private string _soc = "Loading...";
        [ObservableProperty] private string _serialNumber = "Loading...";
        [ObservableProperty] private bool _isSerialHidden = true;

        public DashboardViewModel(IDeviceService deviceService, IAdbService adbService)
        {
            _deviceService = deviceService;
            _adbService = adbService;
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            var props = await _deviceService.GetDevicePropertiesAsync();
            if (props.Count > 0)
            {
                Model = props.GetValueOrDefault("ro.product.model", "Unknown");
                AndroidVersion = props.GetValueOrDefault("ro.build.version.release", "Unknown");
                
                var rawSoc = props.GetValueOrDefault("ro.board.platform", "Unknown");
                if (rawSoc.Equals("mt6768", System.StringComparison.OrdinalIgnoreCase))
                {
                    Soc = "MediaTek MT6768";
                }
                else
                {
                    Soc = rawSoc;
                }

                SerialNumber = props.GetValueOrDefault("ro.serialno", "Unknown");
                OnPropertyChanged(nameof(DisplaySerialNumber));
            }
            else
            {
                Model = "Not Available";
                AndroidVersion = "Not Available";
                Soc = "Not Available";
                SerialNumber = "Not Available";
                OnPropertyChanged(nameof(DisplaySerialNumber));
            }
        }

        [RelayCommand]
        private async Task RestartAdbAsync()
        {
            await _adbService.RestartAdbServerAsync();
            await RefreshAsync();
        }

        [RelayCommand]
        private void ToggleSerial()
        {
            IsSerialHidden = !IsSerialHidden;
        }

        public string DisplaySerialNumber 
        {
            get
            {
                if (!IsSerialHidden) return SerialNumber;
                if (string.IsNullOrEmpty(SerialNumber) || SerialNumber == "Unknown" || SerialNumber == "Not Available") return SerialNumber;
                if (SerialNumber.Length <= 6) return new string('*', SerialNumber.Length);
                return SerialNumber.Substring(0, 2) + "..." + SerialNumber.Substring(SerialNumber.Length - 4);
            }
        }

        partial void OnSerialNumberChanged(string value)
        {
            OnPropertyChanged(nameof(DisplaySerialNumber));
        }

        partial void OnIsSerialHiddenChanged(bool value)
        {
            OnPropertyChanged(nameof(DisplaySerialNumber));
        }

        // Wireless ADB Properties
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
    }
}
