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
    }
}
