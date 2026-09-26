using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.UI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;

namespace RMX3171ControlCentre.ViewModels.Storage
{
    public partial class AppStorageViewModel : ViewModelBase
    {
        private readonly Services.Telemetry.ITelemetryService _telemetryService;
        private readonly IDialogService _dialogService;
        
        public event System.Action? OnBackRequested;

        public ObservableCollection<AppStorageInfo> AllApps { get; } = new();
        public ListCollectionView AppsView { get; }

        [ObservableProperty] private string _applicationStorageTotalText = "Loading...";
        
        [ObservableProperty] private string _searchText = "";
        partial void OnSearchTextChanged(string value) => AppsView.Refresh();

        [ObservableProperty] private string _selectedFilter = "All";
        partial void OnSelectedFilterChanged(string value) => AppsView.Refresh();

        [ObservableProperty] private string _selectedSort = "Total";
        partial void OnSelectedSortChanged(string value) => UpdateSorting();

        [ObservableProperty] private AppStorageInfo? _selectedApp;

        [ObservableProperty] private string _scanStatusText = "Scanning application storage...";
        [ObservableProperty] private bool _isScanning = false;

        public AppStorageViewModel(Services.Telemetry.ITelemetryService telemetryService, IDialogService dialogService)
        {
            _telemetryService = telemetryService;
            _dialogService = dialogService;

            AppsView = new ListCollectionView(AllApps);
            AppsView.Filter = FilterApp;
            UpdateSorting();

            LoadDataFromSnapshot();

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                if (!IsScanning)
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    {
                        dispatcher.BeginInvoke(new Action(LoadDataFromSnapshot));
                    }
                }
            };
        }

        private DateTime _lastLoadedStorageTime = DateTime.MinValue;

        private void LoadDataFromSnapshot()
        {
            var snap = _telemetryService.CurrentSnapshot.Storage;
            if (snap.LastUpdated == System.DateTime.MinValue)
            {
                ScanStatusText = "Application storage scan pending...";
                return;
            }

            var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
            ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";

            if (snap.LastUpdated == _lastLoadedStorageTime)
            {
                // Storage has not changed since last load - skip expensive collection reconciliation
                return;
            }
            _lastLoadedStorageTime = snap.LastUpdated;

            ApplicationStorageTotalText = $"{snap.AppStorageGb:F1} GB";
            
            var oldSelected = SelectedApp?.PackageName;

            // Remove apps no longer present
            var snapAppPackages = new System.Collections.Generic.HashSet<string>(snap.TopApps.Select(a => a.PackageName));
            for (int i = AllApps.Count - 1; i >= 0; i--)
            {
                if (!snapAppPackages.Contains(AllApps[i].PackageName))
                {
                    AllApps.RemoveAt(i);
                }
            }

            // Fast lookup map
            var existingMap = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < AllApps.Count; i++)
            {
                existingMap[AllApps[i].PackageName] = i;
            }

            // Add or update apps
            foreach (var snapApp in snap.TopApps)
            {
                if (existingMap.TryGetValue(snapApp.PackageName, out int index))
                {
                    var existing = AllApps[index];
                    if (existing.SizeMb != snapApp.SizeMb || existing.CacheMb != snapApp.CacheMb || existing.UserDataMb != snapApp.UserDataMb)
                    {
                        AllApps[index] = snapApp;
                    }
                }
                else
                {
                    AllApps.Add(snapApp);
                    existingMap[snapApp.PackageName] = AllApps.Count - 1;
                }
            }

            if (oldSelected != null && SelectedApp?.PackageName != oldSelected)
            {
                SelectedApp = AllApps.FirstOrDefault(a => a.PackageName == oldSelected);
            }
        }

        private bool FilterApp(object obj)
        {
            if (obj is not AppStorageInfo app) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                bool matches = app.AppName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase) ||
                               app.PackageName.Contains(SearchText, System.StringComparison.OrdinalIgnoreCase);
                if (!matches) return false;
            }

            if (SelectedFilter == "User Apps" && app.Type != "User App") return false;
            if (SelectedFilter == "System Apps" && app.Type != "System App") return false;
            if (SelectedFilter == "Cached Data" && app.CacheMb <= 0) return false;
            // "Largest" filter is basically handled by sorting, but if requested as a filter we could show > 100MB
            if (SelectedFilter == "Largest" && app.SizeMb < 100) return false;

            return true;
        }

        private void UpdateSorting()
        {
            AppsView.SortDescriptions.Clear();
            if (SelectedSort == "Total")
                AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageInfo.SizeMb), System.ComponentModel.ListSortDirection.Descending));
            else if (SelectedSort == "User Data")
                AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageInfo.UserDataMb), System.ComponentModel.ListSortDirection.Descending));
            else if (SelectedSort == "Cache")
                AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageInfo.CacheMb), System.ComponentModel.ListSortDirection.Descending));
            else if (SelectedSort == "App Size")
                AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageInfo.AppSizeMb), System.ComponentModel.ListSortDirection.Descending));
        }

        [RelayCommand]
        private void GoBack()
        {
            OnBackRequested?.Invoke();
        }

        [RelayCommand]
        private async Task ScanAppsAsync()
        {
            if (IsScanning) return;
            IsScanning = true;
            ScanStatusText = "Scanning application storage...";
            try
            {
                await _telemetryService.ForceRefreshAsync("All");
                ScanStatusText = "Scan complete.";
            }
            catch (System.Exception ex)
            {
                ScanStatusText = "Application storage scan failed. Reason: " + ex.Message;
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private void ModifyAction(string action)
        {
            _dialogService.ShowMessage("Modification Actions Disabled", 
                "Modification actions will be enabled after read-only storage verification.\n\n" +
                $"You selected: {action}");
        }
    }
}
