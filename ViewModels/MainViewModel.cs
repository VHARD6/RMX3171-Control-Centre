using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Services.Device;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly IDeviceService _deviceService;

        [ObservableProperty]
        private ViewModelBase _currentViewModel;

        [ObservableProperty]
        private string _connectionStatus = "Checking...";

        [ObservableProperty]
        private string _connectionColor = "Gray";

        private readonly IAppModeService _appModeService;
        private readonly IDialogService _dialogService;
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;

        public DashboardViewModel DashboardVM { get; }
        public DeviceInfoViewModel DeviceInfoVM { get; }
        public StorageViewModel StorageVM { get; }
        public BatteryViewModel BatteryVM { get; }
        public CapacityTesterViewModel CapacityTesterVM { get; }
        public MemoryManagerViewModel MemoryManagerVM { get; }
        public AppManagerViewModel AppManagerVM { get; }
        public LogsViewModel LogsVM { get; }

        [ObservableProperty]
        private bool _isAdvancedMode;

        [ObservableProperty]
        private string _modeText = "● READ-ONLY MODE";

        [ObservableProperty]
        private string _modeColor = "LimeGreen";

        public MainViewModel(
            IDeviceService deviceService,
            IAppModeService appModeService,
            IDialogService dialogService,
            RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService,
            DashboardViewModel dashboardVM,
            DeviceInfoViewModel deviceInfoVM,
            StorageViewModel storageVM,
            BatteryViewModel batteryVM,
            CapacityTesterViewModel capacityTesterVM,
            MemoryManagerViewModel memoryManagerVM,
            AppManagerViewModel appManagerVM,
            LogsViewModel logsVM)
        {
            _deviceService = deviceService;
            _appModeService = appModeService;
            _dialogService = dialogService;
            _telemetryService = telemetryService;
            DashboardVM = dashboardVM;
            DeviceInfoVM = deviceInfoVM;
            StorageVM = storageVM;
            BatteryVM = batteryVM;
            CapacityTesterVM = capacityTesterVM;
            MemoryManagerVM = memoryManagerVM;
            AppManagerVM = appManagerVM;
            LogsVM = logsVM;

            CurrentViewModel = DashboardVM;

            _appModeService.ModeChanged += (s, e) =>
            {
                IsAdvancedMode = _appModeService.CurrentMode == Models.AppMode.Advanced;
                ModeText = IsAdvancedMode ? "⚠ ADVANCED MODE" : "● READ-ONLY MODE";
                ModeColor = IsAdvancedMode ? "Orange" : "LimeGreen";
            };

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    var snap = _telemetryService.CurrentSnapshot;
                    if (snap.IsConnected)
                    {
                        ConnectionStatus = $"CONNECTED — {snap.ConnectionTransport}";
                        ConnectionColor = "LimeGreen";
                    }
                    else if (snap.ConnectionTransport == "Unauthorized")
                    {
                        ConnectionStatus = "UNAUTHORIZED";
                        ConnectionColor = "Orange";
                    }
                    else
                    {
                        ConnectionStatus = "NO DEVICE / CONNECTION LOST";
                        ConnectionColor = "Red";
                    }
                });
            };

            _telemetryService.Start();
        }

        [RelayCommand]
        private void ToggleMode()
        {
            if (IsAdvancedMode)
            {
                _appModeService.DisableAdvancedMode();
            }
            else
            {
                var msg = "⚠ ADVANCED MODE\n\nDevice modification features will be enabled.\n\nOnly proceed if you know what you are doing. Modifying system packages or settings can cause instability. Are you sure you want to enter Advanced Mode?";
                var result = System.Windows.MessageBox.Show(msg, "Enable Advanced Mode", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    _appModeService.EnableAdvancedMode();
                }
            }
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
                "AppManager" => AppManagerVM,
                "Logs" => LogsVM,
                _ => DashboardVM
            };

            // Lazy load expensive pages if empty
            if (viewName == "AppManager" && AppManagerVM.Packages.Count == 0)
            {
                _ = AppManagerVM.RefreshCommand.ExecuteAsync(null);
            }
            if (viewName == "Device" && DeviceInfoVM.Properties.Count == 0)
            {
                _ = DeviceInfoVM.LoadPropertiesCommand.ExecuteAsync(null);
            }
        }

    }
}
