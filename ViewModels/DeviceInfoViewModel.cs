using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Services.Device;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;

namespace RMX3171ControlCentre.ViewModels
{
    public class DeviceProperty
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public partial class DeviceInfoViewModel : ViewModelBase
    {
        private readonly IDeviceService _deviceService;

        public ObservableCollection<DeviceProperty> Properties { get; } = new ObservableCollection<DeviceProperty>();

        public DeviceInfoViewModel(IDeviceService deviceService)
        {
            _deviceService = deviceService;
        }

        [RelayCommand]
        private async Task LoadPropertiesAsync()
        {
            Properties.Clear();
            var props = await _deviceService.GetDevicePropertiesAsync();
            foreach (var kvp in props.OrderBy(p => p.Key))
            {
                Properties.Add(new DeviceProperty { Key = kvp.Key, Value = kvp.Value });
            }
        }
    }
}
