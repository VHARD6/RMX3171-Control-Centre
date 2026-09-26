using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.UI;
using RMX3171ControlCentre.ViewModels.Storage;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class StorageViewModel : ViewModelBase
    {
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;
        private readonly IDialogService _dialogService;
        
        // Navigation state within Storage Manager
        [ObservableProperty] private ViewModelBase _currentView;
        [ObservableProperty] private bool _isHubVisible = true;
        [ObservableProperty] private string _navigationPath = "Storage Overview";

        // Hub Properties
        [ObservableProperty] private string _internalTotal = "Loading...";
        [ObservableProperty] private string _internalUsed = "Loading...";
        [ObservableProperty] private string _internalFree = "Loading...";
        [ObservableProperty] private double _internalUsagePercent = 0;
        [ObservableProperty] private string _internalUsageText = "0%";
        [ObservableProperty] private string _internalSummary = "Loading...";

        [ObservableProperty] private string _appStorageText = "0.0 GB";
        [ObservableProperty] private string _otherStorageText = "0.0 GB";
        [ObservableProperty] private string _photosStorageText = "Scan required";
        [ObservableProperty] private string _videosStorageText = "Scan required";
        [ObservableProperty] private string _documentsStorageText = "Scan required";
        [ObservableProperty] private string _downloadsStorageText = "Scan required";
        [ObservableProperty] private string _apksStorageText = "Scan required";

        [ObservableProperty] private string _healthIndicatorText = "Unknown";
        [ObservableProperty] private string _healthIndicatorColor = "Gray";

        [ObservableProperty] private bool _isAdvancedVisible = false;
        [ObservableProperty] private bool _isRawOutputVisible = false;
        
        [ObservableProperty] private string _rawDfOutput = "Waiting for data...";
        [ObservableProperty] private string _rawDiskstatsOutput = "Waiting for data...";
        
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Waiting for data...";

        public ObservableCollection<AdvancedMountInfo> AdvancedMounts { get; } = new();

        private readonly AppStorageViewModel _appStorageViewModel;

        public StorageViewModel(RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService, IDialogService dialogService, AppStorageViewModel appStorageViewModel)
        {
            _telemetryService = telemetryService;
            _dialogService = dialogService;
            _appStorageViewModel = appStorageViewModel;
            _appStorageViewModel.OnBackRequested += () => NavigateToCategory("Hub");
            CurrentView = null; // Fix infinite visual tree recursion by NOT setting it to 'this'

            DateTime lastLoadedTime = DateTime.MinValue;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var snap = _telemetryService.CurrentSnapshot.Storage;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (snap.LastUpdated == System.DateTime.MinValue) return;

                    var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                    LastUpdatedText = secondsAgo > 20 ? $"Last updated {secondsAgo:F0}s ago" : "Live";

                    if (snap.LastUpdated == lastLoadedTime) return;
                    lastLoadedTime = snap.LastUpdated;

                    InternalTotal = $"{snap.TotalGb:F1} GB";
                    InternalUsed = $"{snap.UsedGb:F1} GB";
                    InternalFree = $"{snap.FreeGb:F1} GB";
                    InternalSummary = $"{snap.UsedGb:F1} GB / {snap.TotalGb:F1} GB used";
                    
                    AppStorageText = $"{snap.AppStorageGb:F1} GB";
                    OtherStorageText = $"{snap.OtherStorageGb:F1} GB";

                    if (snap.TotalGb > 0)
                    {
                        InternalUsagePercent = snap.UsagePercentage;
                        InternalUsageText = $"{snap.UsagePercentage:F1}%";

                        if (snap.UsagePercentage < 70)
                        {
                            HealthIndicatorText = "Healthy free space";
                            HealthIndicatorColor = "LimeGreen";
                        }
                        else if (snap.UsagePercentage < 85)
                        {
                            HealthIndicatorText = "Getting full";
                            HealthIndicatorColor = "Gold";
                        }
                        else if (snap.UsagePercentage < 95)
                        {
                            HealthIndicatorText = "Low free space";
                            HealthIndicatorColor = "Orange";
                        }
                        else
                        {
                            HealthIndicatorText = "Very low free space";
                            HealthIndicatorColor = "Red";
                        }
                    }

                    AdvancedMounts.Clear();
                    foreach (var m in snap.AdvancedMounts)
                    {
                        AdvancedMounts.Add(m);
                    }

                    RawDfOutput = snap.RawDfOutput;
                    RawDiskstatsOutput = snap.RawDiskstatsOutput;
                }));
            };
        }

        [RelayCommand]
        private void ToggleAdvanced() => IsAdvancedVisible = !IsAdvancedVisible;

        [RelayCommand]
        private void ToggleRawOutput() => IsRawOutputVisible = !IsRawOutputVisible;

        [RelayCommand]
        private async Task RefreshStorageAsync()
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

        [RelayCommand]
        private void NavigateToCategory(string category)
        {
            if (category == "Hub")
            {
                IsHubVisible = true;
                NavigationPath = "Storage Overview";
                CurrentView = null;
                return;
            }

            IsHubVisible = false;
            NavigationPath = $"Storage Overview > {category}";

            if (category == "Apps")
            {
                CurrentView = _appStorageViewModel;
                return;
            }
            
            // Temporary dialog until implemented
            _dialogService.ShowMessage("Under Construction", $"{category} section is currently being built.");
            NavigateToCategory("Hub");
        }
    }
}
