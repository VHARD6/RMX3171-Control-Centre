using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.ViewModels.Storage
{
    public partial class AppStorageViewModel : ViewModelBase
    {
        private readonly IAdbService _adbService;
        public event Action? OnBackRequested;
        public event Action<double>? OnTotalAppStorageUpdated;

        public ObservableCollection<AppStorageItem> AllApps { get; } = new();
        public ListCollectionView AppsView { get; }

        [ObservableProperty] private string _scanStatusText = "No app storage scan performed.";
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private AppStorageItem? _selectedApp;
        partial void OnSelectedAppChanged(AppStorageItem? value) => OnPropertyChanged(nameof(HasSelectedApp));
        public bool HasSelectedApp => SelectedApp != null;

        [ObservableProperty] private string _searchQuery = "";
        partial void OnSearchQueryChanged(string value) => AppsView.Refresh();

        public ObservableCollection<string> Filters { get; } = new(new[] { "All", "User Apps", "System Apps" });
        [ObservableProperty] private string _selectedFilter = "All";
        partial void OnSelectedFilterChanged(string value) => AppsView.Refresh();

        public ObservableCollection<string> SortOptions { get; } = new(new[] { "Total", "User Data", "Cache", "App Size", "Application name" });
        [ObservableProperty] private string _selectedSort = "Total";
        partial void OnSelectedSortChanged(string value) => UpdateSorting();

        private CancellationTokenSource? _scanCts;
        private DateTime _lastScanTime = DateTime.MinValue;
        private double _totalAppStorageGb = 0;

        public AppStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;
            AppsView = new ListCollectionView(AllApps);
            AppsView.Filter = FilterApp;
            UpdateSorting();
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllApps.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - _lastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanAppsAsync();
            }
        }

        public void OnNavigatedFrom()
        {
            CancelScan();
        }

        [RelayCommand]
        private void GoBack()
        {
            OnBackRequested?.Invoke();
        }

        [RelayCommand]
        private void CancelScan()
        {
            if (IsScanning && _scanCts != null)
            {
                _scanCts.Cancel();
            }
        }

        [RelayCommand]
        private async Task ScanAppsAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning application storage...";
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            long startMemory = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();

            try
            {
                var sysResult = await _adbService.ExecuteCommandAsync("shell pm list packages -s", true, token);
                var sysPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (sysResult.ExitCode == 0)
                {
                    var lines = sysResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("package:")) sysPackages.Add(line.Substring(8));
                    }
                }
                
                token.ThrowIfCancellationRequested();

                var diskResult = await _adbService.ExecuteCommandAsync("shell dumpsys diskstats", true, token);
                if (diskResult.ExitCode != 0 || string.IsNullOrWhiteSpace(diskResult.Output))
                {
                    throw new Exception("dumpsys diskstats failed or returned empty.");
                }

                token.ThrowIfCancellationRequested();

                var pkgs = ParseArray(diskResult.Output, "Package Names");
                var apps = ParseArray(diskResult.Output, "App Sizes");
                var datas = ParseArray(diskResult.Output, "App Data Sizes");
                var caches = ParseArray(diskResult.Output, "Cache Sizes");

                if (pkgs.Length == 0 || pkgs.Length != apps.Length)
                {
                    throw new Exception("Failed to parse diskstats arrays or length mismatch.");
                }

                var newApps = new List<AppStorageItem>();
                double totalBytes = 0;

                for (int i = 0; i < pkgs.Length; i++)
                {
                    string pkg = pkgs[i].Trim('"');
                    long appSize = ParseLong(apps[i]);
                    long dataSize = ParseLong(datas[i]);
                    long cacheSize = ParseLong(caches[i]);

                    totalBytes += appSize + dataSize + cacheSize;

                    newApps.Add(new AppStorageItem
                    {
                        Package = pkg,
                        Application = GetFriendlyName(pkg),
                        Type = sysPackages.Contains(pkg) ? "System App" : "User App",
                        AppSizeMb = appSize / 1048576.0,
                        UserDataMb = dataSize / 1048576.0,
                        CacheMb = cacheSize / 1048576.0
                    });
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                {
                    dispatcher.Invoke(() =>
                    {
                        AllApps.Clear();
                        foreach (var app in newApps) AllApps.Add(app);
                        UpdateSorting();
                        
                        _lastScanTime = DateTime.Now;
                        ScanStatusText = $"Last scanned: 0 seconds ago";
                        IsLoaded = true;

                        _totalAppStorageGb = totalBytes / 1073741824.0;
                        OnTotalAppStorageUpdated?.Invoke(_totalAppStorageGb);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Application storage scan failed.\nReason: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;

                sw.Stop();
                long endMemory = GC.GetTotalMemory(false);
            }
        }

        private string[] ParseArray(string output, string arrayName)
        {
            var match = Regex.Match(output, $@"{arrayName}:\s*\[([^\]]+)\]");
            if (match.Success) return match.Groups[1].Value.Split(',');
            return Array.Empty<string>();
        }

        private long ParseLong(string val) => long.TryParse(val, out long res) ? res : 0;

        private string GetFriendlyName(string packageName)
        {
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "com.whatsapp", "WhatsApp" },
                { "com.google.android.apps.maps", "Google Maps" },
                { "com.instagram.android", "Instagram" },
                { "com.google.android.apps.photos", "Google Photos" },
                { "com.discord", "Discord" },
                { "com.spotify.music", "Spotify" },
                { "com.google.android.youtube", "YouTube" },
                { "com.android.chrome", "Google Chrome" }
            };
            if (mappings.TryGetValue(packageName, out var name)) return name;

            var parts = packageName.Split('.');
            var last = parts.LastOrDefault() ?? packageName;
            if (last.Length > 1) return char.ToUpper(last[0]) + last.Substring(1);
            return packageName;
        }

        private bool FilterApp(object obj)
        {
            if (obj is not AppStorageItem app) return false;
            
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                if (!app.Application.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) &&
                    !app.Package.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (SelectedFilter == "User Apps" && app.Type != "User App") return false;
            if (SelectedFilter == "System Apps" && app.Type != "System App") return false;

            return true;
        }

        private void UpdateSorting()
        {
            AppsView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Total":
                    AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageItem.TotalMb), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case "User Data":
                    AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageItem.UserDataMb), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case "Cache":
                    AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageItem.CacheMb), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case "App Size":
                    AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageItem.AppSizeMb), System.ComponentModel.ListSortDirection.Descending));
                    break;
                case "Application name":
                    AppsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(AppStorageItem.Application), System.ComponentModel.ListSortDirection.Ascending));
                    break;
            }
        }
    }
}
