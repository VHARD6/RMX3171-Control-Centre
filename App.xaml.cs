using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using RMX3171ControlCentre.Services.Adb;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.ViewModels;
using RMX3171ControlCentre.Views;

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
            services.AddSingleton<IAdbService, AdbService>();
            services.AddSingleton<IDeviceService, DeviceService>();
            services.AddSingleton<IBatteryService, BatteryService>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<DeviceInfoViewModel>();
            services.AddTransient<StorageViewModel>();
            services.AddTransient<BatteryViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<CapacityTesterViewModel>();

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
