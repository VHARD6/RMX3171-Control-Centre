using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Services.Device;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IDeviceService _deviceService;
        private DispatcherTimer _timer;

        [ObservableProperty]
        private ViewModelBase _currentViewModel;

        [ObservableProperty]
        private string _connectionStatus = "Checking...";

        [ObservableProperty]
        private string _connectionColor = "Gray";

        public DashboardViewModel DashboardVM { get; }
        public DeviceInfoViewModel DeviceInfoVM { get; }
        public StorageViewModel StorageVM { get; }
        public BatteryViewModel BatteryVM { get; }
        public CapacityTesterViewModel CapacityTesterVM { get; }
        public MemoryManagerViewModel MemoryManagerVM { get; }
        public LogsViewModel LogsVM { get; }

        public MainViewModel(
            IDeviceService deviceService,
            DashboardViewModel dashboardVM,
            DeviceInfoViewModel deviceInfoVM,
            StorageViewModel storageVM,
            BatteryViewModel batteryVM,
            CapacityTesterViewModel capacityTesterVM,
            MemoryManagerViewModel memoryManagerVM,
            LogsViewModel logsVM)
        {
            _deviceService = deviceService;
            DashboardVM = dashboardVM;
            DeviceInfoVM = deviceInfoVM;
            StorageVM = storageVM;
            BatteryVM = batteryVM;
            CapacityTesterVM = capacityTesterVM;
            MemoryManagerVM = memoryManagerVM;
            LogsVM = logsVM;

            CurrentViewModel = DashboardVM;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (s, e) => await CheckConnectionAsync();
            _timer.Start();

            // Initial check
            _ = CheckConnectionAsync();
        }

        [RelayCommand]
        private void Navigate(string viewName)
        {
            CurrentViewModel = viewName switch
            {
                "Dashboard" => DashboardVM,
                "Device" => DeviceInfoVM,
                "Storage" => StorageVM,
                "Battery" => BatteryVM,
                "Capacity" => CapacityTesterVM,
                "Memory" => MemoryManagerVM,
                "Logs" => LogsVM,
                _ => DashboardVM
            };
        }

        private async Task CheckConnectionAsync()
        {
            var state = await _deviceService.GetConnectionStateAsync();
            
            ConnectionStatus = state switch
            {
                ConnectionState.CONNECTED_USB => "CONNECTED — USB",
                ConnectionState.CONNECTED_WIFI => "CONNECTED — Wi-Fi",
                ConnectionState.CONNECTED_UNKNOWN => "CONNECTED",
                ConnectionState.UNAUTHORIZED => "UNAUTHORIZED",
                ConnectionState.OFFLINE => "OFFLINE",
                ConnectionState.NO_DEVICE => "NO DEVICE",
                _ => "UNKNOWN"
            };
            
            ConnectionColor = state switch
            {
                ConnectionState.CONNECTED_USB => "LimeGreen",
                ConnectionState.CONNECTED_WIFI => "Cyan",
                ConnectionState.CONNECTED_UNKNOWN => "LimeGreen",
                ConnectionState.UNAUTHORIZED => "Orange",
                ConnectionState.OFFLINE => "Red",
                ConnectionState.NO_DEVICE => "Gray",
                _ => "Gray"
            };
        }
    }
}
