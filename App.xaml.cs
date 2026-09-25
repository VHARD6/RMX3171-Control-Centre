using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using RMX3171ControlCentre.Services.Adb;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.ViewModels;
using RMX3171ControlCentre.Views;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;

namespace RMX3171ControlCentre
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(ServiceCollection services)
        {
            // Services
            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IAuditService, AuditService>();
            services.AddSingleton<IAppModeService, AppModeService>();
            services.AddSingleton<RMX3171ControlCentre.Services.Telemetry.ITelemetryService, RMX3171ControlCentre.Services.Telemetry.TelemetryService>();
            services.AddSingleton<IAdbService, AdbService>();
            services.AddSingleton<IDeviceService, DeviceService>();
            services.AddSingleton<IBatteryService, BatteryService>();
            services.AddSingleton<IMemoryService, MemoryService>();
            services.AddSingleton<IAppManagerService, AppManagerService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<DeviceInfoViewModel>();
            services.AddTransient<StorageViewModel>();
            services.AddTransient<BatteryViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<CapacityTesterViewModel>();
            services.AddTransient<MemoryManagerViewModel>();
            services.AddTransient<AppManagerViewModel>();

            // Views
            services.AddTransient<MainWindow>();
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}
