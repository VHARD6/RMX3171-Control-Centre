using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    public class ApkRow
    {
        public List<ApkItem> Items { get; set; } = new();
    }

    public partial class ApksStorageViewModel : ViewModelBase, IDisposable
    {
        private readonly IAdbService _adbService;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalApkStorageUpdated;

        // Models
        public ObservableCollection<ApkFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<ApkItem> AllApks { get; } = new();
        public ObservableCollection<ApkItem> FolderApks { get; } = new();
        public ListCollectionView FolderApksView { get; }

        // Virtualized Grid Rows
        public ObservableCollection<ApkRow> GridRows { get; } = new();
        private int _lastGridColumns = 1;

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private bool _isGridView = true;
        [ObservableProperty] private string _scanStatusText = "No apk scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Apks";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalApksCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private ApkFolderGroup? _currentFolderGroup;

        private CancellationTokenSource? _scanCts;

        // View Mode Toggles
        [RelayCommand] private void ShowGridView() => IsGridView = true;
        [RelayCommand] private void ShowListView() => IsGridView = false;

        // Viewer States
        [ObservableProperty] private bool _viewerVisible;
        public event Action? OnViewerClosed;
        partial void OnViewerVisibleChanged(bool value)
        {
            if (!value) OnViewerClosed?.Invoke();
        }
        [ObservableProperty] private bool _viewerIsLoading;
        [ObservableProperty] private string _viewerLoadingText = "Preparing apk...";
        [ObservableProperty] private ApkItem? _viewerApk;
        [ObservableProperty] private string? _viewerLocalPath;

        private CancellationTokenSource? _transferCts;
        private readonly string _apkCacheDir;

        // Selection State
        [ObservableProperty] private ApkItem? _selectedApk;
        partial void OnSelectedApkChanged(ApkItem? value) => OnPropertyChanged(nameof(HasSelectedApk));
        public bool HasSelectedApk => SelectedApk != null;

        [ObservableProperty] private string _selectionSummaryText = "0 selected";

        // Selection Handlers (called from Code Behind)
        private ApkItem? _lastClickedApk;
        
        public void HandleApkClick(ApkItem apk, bool isCtrlPressed, bool isShiftPressed)
        {
            if (apk == null) return;

            var allVisible = FolderApksView.Cast<ApkItem>().ToList();
            if (allVisible.Count == 0) return;

            if (isCtrlPressed)
            {
                apk.IsSelected = !apk.IsSelected;
                SelectedApk = apk.IsSelected ? apk : allVisible.FirstOrDefault(p => p.IsSelected);
                _lastClickedApk = apk;
            }
            else if (isShiftPressed && _lastClickedApk != null)
            {
                int startIndex = allVisible.IndexOf(_lastClickedApk);
                int endIndex = allVisible.IndexOf(apk);
                
                if (startIndex != -1 && endIndex != -1)
                {
                    int min = Math.Min(startIndex, endIndex);
                    int max = Math.Max(startIndex, endIndex);
                    
                    // Clear existing selection first
                    foreach (var p in allVisible) p.IsSelected = false;
                    
                    for (int i = min; i <= max; i++)
                    {
                        allVisible[i].IsSelected = true;
                    }
                    SelectedApk = apk;
                }
            }
            else
            {
                foreach (var p in allVisible) p.IsSelected = false;
                apk.IsSelected = true;
                SelectedApk = apk;
                _lastClickedApk = apk;
            }

            UpdateSelectionSummary();
        }

        public async void HandleApkDoubleClick(ApkItem apk)
        {
            if (apk == null) return;
            
            if (_transferCts != null)
            {
                _transferCts.Cancel();
                _transferCts.Dispose();
                _transferCts = null;
            }
            
            if (ViewerLocalPath != null)
            {
                ViewerLocalPath = null;
            }

            ViewerApk = apk;
            ViewerVisible = true;
            ViewerIsLoading = true;
            ViewerLoadingText = $"Transferring {apk.SizeText}...";
            
            _transferCts = new CancellationTokenSource();
            var token = _transferCts.Token;

            try
            {
                if (!Directory.Exists(_apkCacheDir))
                    Directory.CreateDirectory(_apkCacheDir);
                    
                string ext = Path.GetExtension(apk.FileName);
                if (string.IsNullOrEmpty(ext)) ext = apk.Extension;
                
                string tempFile = Path.Combine(_apkCacheDir, Guid.NewGuid().ToString("N") + ext);
                
                var pullCmd = $"pull \"{apk.FullPath}\" \"{tempFile}\"";
                var result = await _adbService.ExecuteCommandAsync(pullCmd, true, token);
                
                token.ThrowIfCancellationRequested();
                
                if (result.ExitCode != 0)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        System.Windows.MessageBox.Show($"Transfer failed:\n{result.Output}", "Transfer Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
                else if (File.Exists(tempFile))
                {
                    ViewerLocalPath = tempFile;
                    
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempFile,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                            System.Windows.MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        });
                    }
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        System.Windows.MessageBox.Show("Transfer failed. File was not transferred.", "Transfer Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        System.Windows.MessageBox.Show($"An error occurred:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    });
                }
            }
            finally
            {
                ViewerIsLoading = false;
                ViewerVisible = false;
                
                if (_transferCts != null)
                {
                    _transferCts.Dispose();
                    _transferCts = null;
                }
            }
        }

        [RelayCommand]
        private async Task CloseViewerAsync()
        {
            if (_transferCts != null)
            {
                _transferCts.Cancel();
                _transferCts.Dispose();
                _transferCts = null;
            }

            ViewerVisible = false;
            
            // Do NOT delete the temporary file here! 
            // External applications need time to launch and lock the file.
            // CleanupCacheDir() handles garbage collection on the next app launch.
            ViewerLocalPath = null;
            ViewerApk = null;
            
            await Task.CompletedTask;
        }

        public void UpdateSelection(IEnumerable<ApkItem> items)
        {
            var allVisible = FolderApksView.Cast<ApkItem>().ToList();
            foreach (var p in allVisible) p.IsSelected = false;
            
            foreach (var item in items)
            {
                if (item != null) item.IsSelected = true;
            }
            
            SelectedApk = items.LastOrDefault();
            _lastClickedApk = SelectedApk;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = FolderApksView.Cast<ApkItem>().Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                SelectionSummaryText = "0 selected";
            }
            else
            {
                long totalBytes = selected.Sum(x => x.SizeBytes);
                SelectionSummaryText = $"{selected.Count:N0} selected — {FormatSize(totalBytes)}";
            }
        }

        public void UpdateGridColumns(int columns)
        {
            if (columns < 1) columns = 1;
            _lastGridColumns = columns;
            
            var allVisible = FolderApksView.Cast<ApkItem>().ToList();
            GridRows.Clear();
            
            for (int i = 0; i < allVisible.Count; i += columns)
            {
                var rowItems = allVisible.Skip(i).Take(columns).ToList();
                GridRows.Add(new ApkRow { Items = rowItems });
            }
        }

        // Storage Source Filter
        public ObservableCollection<string> StorageFilters { get; } = new(new[]
        {
            "All Storage",
            "Internal Storage"
        });
        [ObservableProperty] private string _selectedStorageFilter = "All Storage";
        partial void OnSelectedStorageFilterChanged(string value)
        {
            FolderGroupsView.Refresh();
            if (!IsFolderView && CurrentFolderGroup != null)
            {
                OpenFolder(CurrentFolderGroup);
            }
        }

        // Search
        [ObservableProperty] private string _searchQuery = "";
        partial void OnSearchQueryChanged(string value)
        {
            if (IsFolderView) FolderGroupsView.Refresh();
            else
            {
                FolderApksView.Refresh();
                UpdateGridColumns(_lastGridColumns);
            }
        }

        // Sort Options
        public ObservableCollection<string> SortOptions { get; } = new(new[]
        {
            "Newest",
            "Oldest",
            "Size (Largest)",
            "Size (Smallest)",
            "Name",
            "Type"
        });

        [ObservableProperty] private string _selectedSort = "Newest";
        partial void OnSelectedSortChanged(string value)
        {
            UpdateSorting();
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        public ApksStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;

            _apkCacheDir = Path.Combine(Path.GetTempPath(), "RMX3171_ApkCache");
            Task.Run(() => CleanupCacheDir());

            FolderGroupsView = new ListCollectionView(FolderGroups) { Filter = FilterFolder };
            FolderApksView = new ListCollectionView(FolderApks) { Filter = FilterApk };

            UpdateSorting();
        }

        private void CleanupCacheDir()
        {
            try
            {
                if (Directory.Exists(_apkCacheDir))
                {
                    foreach (var file in Directory.GetFiles(_apkCacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllApks.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - LastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanApksAsync();
            }
        }

        public void OnNavigatedFrom()
        {
            CancelScan();
        }

        [RelayCommand]
        private void GoBack()
        {
            CancelScan();
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
        private void BackToFolders()
        {
            
            IsFolderView = true;
            CurrentFolderGroup = null;
            SelectedApk = null;
            UpdateSelection(Array.Empty<ApkItem>());
            CurrentBreadcrumb = "Storage > Apks";
            SearchQuery = "";
            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanApksAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning apk metadata...";
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            
            try
            {
                const string queryCmd = "shell \"content query --uri content://media/external/file --projection _id:_data:_size:date_modified:mime_type:bucket_display_name:_display_name\"";
                var queryResult = await _adbService.ExecuteCommandAsync(queryCmd, isReadOnly: true, token);
                token.ThrowIfCancellationRequested();

                if (queryResult.ExitCode != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(queryResult.Output) ? "Unable to enumerate this location." : queryResult.Output.Trim());

                if (string.IsNullOrWhiteSpace(queryResult.Output) || queryResult.Output.Contains("No result found"))
                {
                    ApplyResults(new List<ApkItem>(), new List<ApkFolderGroup>(), 0, 0, false);
                    ScanStatusText = "No apks found on device.";
                    return;
                }

                var regex = new Regex(@"^Row: \d+ _id=(.*?), _data=(.*?), _size=(\d*), date_modified=(\d*), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*)$", RegexOptions.Compiled);
                var parsedDocs = new List<ApkItem>();
                var groupDict = new Dictionary<string, (int count, long bytes, StorageLocation loc, string folder)>(StringComparer.OrdinalIgnoreCase);

                long totalBytes = 0;
                bool hasSdCard = false;

                using (var reader = new StringReader(queryResult.Output))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        var match = regex.Match(line);
                        if (!match.Success) continue;

                        string data = match.Groups[2].Value.Trim();
                        string ext = Path.GetExtension(data)?.ToLowerInvariant() ?? "";
                        
                        bool isApk = data.EndsWith(".apk", StringComparison.OrdinalIgnoreCase);
                        if (!isApk) continue;

                        string id = match.Groups[1].Value.Trim();
                        long size = long.TryParse(match.Groups[3].Value, out long s) ? s : 0;
                        long dateSec = long.TryParse(match.Groups[4].Value, out long d) ? d : 0;
                        string mime = match.Groups[5].Value.Trim();
                        string bucket = match.Groups[6].Value.Trim();
                        string name = match.Groups[7].Value.Trim();

                        if (string.IsNullOrWhiteSpace(bucket) || bucket.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { bucket = Path.GetFileName(Path.GetDirectoryName(data)) ?? "Other"; } catch { bucket = "Other"; }
                        }
                        if (string.IsNullOrWhiteSpace(name) || name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { name = Path.GetFileName(data); } catch { name = "Apk"; }
                        }

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try { dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime; } catch { }
                        }

                        var (loc, vol) = GetStorageInfo(data);
                        if (loc == StorageLocation.ExternalSd) hasSdCard = true;

                        var doc = new ApkItem
                        {
                            Id = id,
                            FileName = name,
                            FullPath = data,
                            Folder = bucket,
                            SizeBytes = size,
                            DateModified = dateModified,
                            MimeType = mime.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : mime,
                            Extension = ext,
                            Location = loc,
                            VolumeId = vol
                        };

                        parsedDocs.Add(doc);
                        totalBytes += size;

                        string groupKey = $"{bucket}|{loc}";
                        if (groupDict.TryGetValue(groupKey, out var existing))
                            groupDict[groupKey] = (existing.count + 1, existing.bytes + size, loc, bucket);
                        else
                            groupDict[groupKey] = (1, size, loc, bucket);
                    }
                }

                var folderList = new List<ApkFolderGroup>();
                if (parsedDocs.Count > 0)
                {
                    folderList.Add(new ApkFolderGroup
                    {
                        FolderName = "All Discovered APKs",
                        ApkCount = parsedDocs.Count,
                        TotalSizeBytes = totalBytes,
                        Location = StorageLocation.Unknown
                    });
                }

                foreach (var kvp in groupDict.Values.OrderByDescending(g => g.bytes))
                {
                    folderList.Add(new ApkFolderGroup
                    {
                        FolderName = kvp.folder,
                        ApkCount = kvp.count,
                        TotalSizeBytes = kvp.bytes,
                        Location = kvp.loc
                    });
                }

                ApplyResults(parsedDocs, folderList, totalBytes, parsedDocs.Count, hasSdCard);

                LastScanTime = DateTime.Now;
                ScanStatusText = "Last scanned: 0 seconds ago";
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Apks scan failed.\nReason: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        private static (StorageLocation, string) GetStorageInfo(string path)
        {
            if (path.StartsWith("/storage/emulated/0", StringComparison.OrdinalIgnoreCase)) return (StorageLocation.Internal, "emulated/0");
            
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Equals("storage", StringComparison.OrdinalIgnoreCase))
            {
                if (!parts[1].Equals("emulated", StringComparison.OrdinalIgnoreCase) && !parts[1].Equals("self", StringComparison.OrdinalIgnoreCase))
                    return (StorageLocation.ExternalSd, parts[1]);
            }
            return (StorageLocation.Unknown, "Unknown");
        }

        private void ApplyResults(List<ApkItem> docs, List<ApkFolderGroup> groups, long totalBytes, int totalCount, bool hasSdCard)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.Invoke(() =>
                {
                    if (hasSdCard && !StorageFilters.Contains("SD Card")) StorageFilters.Add("SD Card");
                    else if (!hasSdCard && StorageFilters.Contains("SD Card"))
                    {
                        if (SelectedStorageFilter == "SD Card") SelectedStorageFilter = "All Storage";
                        StorageFilters.Remove("SD Card");
                    }

                    AllApks.Clear();
                    AllApks.AddRange(docs);

                    FolderGroups.Clear();
                    foreach (var g in groups) FolderGroups.Add(g);

                    TotalStorageSummaryText = FormatSize(totalBytes);
                    TotalApksCountText = $"{totalCount:N0} apks";
                    IsLoaded = true;

                    OnTotalApkStorageUpdated?.Invoke(totalBytes / (1024.0 * 1024.0 * 1024.0), totalCount);

                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not ApkFolderGroup group) return false;
            
            if (SelectedStorageFilter == "Internal Storage" && group.Location != StorageLocation.Internal && group.Location != StorageLocation.Unknown) return false;
            if (SelectedStorageFilter == "SD Card" && group.Location != StorageLocation.ExternalSd && group.Location != StorageLocation.Unknown) return false;
            
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterApk(object obj)
        {
            if (obj is not ApkItem doc) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return doc.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || doc.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderApksView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.SizeBytes), ListSortDirection.Descending)); break;
                case "Size (Smallest)": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.SizeBytes), ListSortDirection.Ascending)); break;
                case "Newest": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.DateModified), ListSortDirection.Descending)); break;
                case "Oldest": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.DateModified), ListSortDirection.Ascending)); break;
                case "Name": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.FileName), ListSortDirection.Ascending)); break;
                case "Type": FolderApksView.SortDescriptions.Add(new SortDescription(nameof(ApkItem.Extension), ListSortDirection.Ascending)); break;
            }
            if (!IsFolderView)
            {
                UpdateGridColumns(_lastGridColumns);
            }
        }

        public DateTime LastScanTime { get; private set; } = DateTime.MinValue;

        
        public void Dispose()
        {
            _scanCts?.Dispose();
        }

        [RelayCommand]
        private void OpenFolder(ApkFolderGroup folder)
        {
            if (folder == null) return;
            
            CurrentFolderGroup = folder;
            IsFolderView = false;
            
            SelectedApk = null;
            UpdateSelection(Array.Empty<ApkItem>());
            
            string sourcePrefix = folder.LocationLabel;
            if (folder.FolderName == "All Discovered APKs")
                CurrentBreadcrumb = "Apks > All Discovered";
            else
                CurrentBreadcrumb = $"Apks > {sourcePrefix} > {folder.FolderName}";
                
            SelectedFolderSummaryText = $"{folder.ApkCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            FolderApks.Clear();
            IEnumerable<ApkItem> items = folder.FolderName == "All Discovered APKs" 
                ? AllApks 
                : AllApks.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase) && p.Location == folder.Location);

            if (SelectedStorageFilter == "Internal Storage")
                items = items.Where(p => p.Location == StorageLocation.Internal);
            else if (SelectedStorageFilter == "SD Card")
                items = items.Where(p => p.Location == StorageLocation.ExternalSd);

            foreach (var item in items) FolderApks.Add(item);

            UpdateSorting();
        }

            }
}
