using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class MemoryManagerViewModel : ViewModelBase
    {
        private readonly IMemoryService _memoryService;

        [ObservableProperty]
        private MemoryInfo _currentMemoryInfo = new MemoryInfo();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _topConsumers = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private bool _isRefreshing;

        public MemoryManagerViewModel(IMemoryService memoryService)
        {
            _memoryService = memoryService;
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                CurrentMemoryInfo = await _memoryService.GetMemoryInfoAsync();
                
                var consumers = await _memoryService.GetTopConsumersAsync();
                TopConsumers.Clear();
                foreach (var c in consumers)
                {
                    TopConsumers.Add(c);
                }
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task ForceStopAsync(AppProcessInfo app)
        {
            if (app == null || app.IsProtected || IsRefreshing) return;

            var result = MessageBox.Show($"Are you sure you want to force stop {app.PackageName}?\n\nThis will terminate the application and its background services.", "Confirm Force Stop", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                bool success = await _memoryService.ForceStopAppAsync(app.PackageName);
                if (success)
                {
                    MessageBox.Show($"Successfully closed {app.PackageName}. RAM released.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshAsync();
                }
                else
                {
                    MessageBox.Show($"Failed to force stop {app.PackageName}.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
