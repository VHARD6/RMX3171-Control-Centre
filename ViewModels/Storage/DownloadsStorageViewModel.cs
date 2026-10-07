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
    public class DownloadRow
    {
        public List<DownloadItem> Items { get; set; } = new();
    }

    public partial class DownloadsStorageViewModel : ViewModelBase, IDisposable
    {
        private readonly IAdbService _adbService;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalDownloadStorageUpdated;

        // Models
        public ObservableCollection<DownloadFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<DownloadItem> AllDownloads { get; } = new();
        public ObservableCollection<DownloadItem> FolderDownloads { get; } = new();
        public ListCollectionView FolderDownloadsView { get; }

        // Virtualized Grid Rows
        public ObservableCollection<DownloadRow> GridRows { get; } = new();
        private int _lastGridColumns = 1;

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private bool _isGridView = true;
        [ObservableProperty] private string _scanStatusText = "No download scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Downloads";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalDownloadsCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private DownloadFolderGroup? _currentFolderGroup;

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
        [ObservableProperty] private string _viewerLoadingText = "Preparing download...";
        [ObservableProperty] private DownloadItem? _viewerDownload;
        [ObservableProperty] private string? _viewerLocalPath;

        private CancellationTokenSource? _transferCts;
        private readonly string _downloadCacheDir;

        // Selection State
        [ObservableProperty] private DownloadItem? _selectedDownload;
        partial void OnSelectedDownloadChanged(DownloadItem? value) => OnPropertyChanged(nameof(HasSelectedDownload));
        public bool HasSelectedDownload => SelectedDownload != null;

        [ObservableProperty] private string _selectionSummaryText = "0 selected";

        // Selection Handlers (called from Code Behind)
        private DownloadItem? _lastClickedDownload;
        
        public void HandleDownloadClick(DownloadItem download, bool isCtrlPressed, bool isShiftPressed)
        {
            if (download == null) return;

            var allVisible = FolderDownloadsView.Cast<DownloadItem>().ToList();
            if (allVisible.Count == 0) return;

            if (isCtrlPressed)
            {
                download.IsSelected = !download.IsSelected;
                SelectedDownload = download.IsSelected ? download : allVisible.FirstOrDefault(p => p.IsSelected);
                _lastClickedDownload = download;
            }
            else if (isShiftPressed && _lastClickedDownload != null)
            {
                int startIndex = allVisible.IndexOf(_lastClickedDownload);
                int endIndex = allVisible.IndexOf(download);
                
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
                    SelectedDownload = download;
                }
            }
            else
            {
                foreach (var p in allVisible) p.IsSelected = false;
                download.IsSelected = true;
                SelectedDownload = download;
                _lastClickedDownload = download;
            }

            UpdateSelectionSummary();
        }

        public async void HandleDownloadDoubleClick(DownloadItem download)
        {
            if (download == null) return;
            
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

            ViewerDownload = download;
            ViewerVisible = true;
            ViewerIsLoading = true;
            ViewerLoadingText = $"Transferring {download.SizeText}...";
            
            _transferCts = new CancellationTokenSource();
            var token = _transferCts.Token;

            try
            {
                if (!Directory.Exists(_downloadCacheDir))
                    Directory.CreateDirectory(_downloadCacheDir);
                    
                string ext = Path.GetExtension(download.FileName);
                if (string.IsNullOrEmpty(ext)) ext = download.Extension;
                
                string tempFile = Path.Combine(_downloadCacheDir, Guid.NewGuid().ToString("N") + ext);
                
                var pullCmd = $"pull \"{download.FullPath}\" \"{tempFile}\"";
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
                        System.Windows.MessageBox.Show("Transfer failed. File was not downloaded.", "Transfer Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            ViewerDownload = null;
            
            await Task.CompletedTask;
        }

        public void UpdateSelection(IEnumerable<DownloadItem> items)
        {
            var allVisible = FolderDownloadsView.Cast<DownloadItem>().ToList();
            foreach (var p in allVisible) p.IsSelected = false;
            
            foreach (var item in items)
            {
                if (item != null) item.IsSelected = true;
            }
            
            SelectedDownload = items.LastOrDefault();
            _lastClickedDownload = SelectedDownload;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = FolderDownloadsView.Cast<DownloadItem>().Where(p => p.IsSelected).ToList();
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
            
            var allVisible = FolderDownloadsView.Cast<DownloadItem>().ToList();
            GridRows.Clear();
            
            for (int i = 0; i < allVisible.Count; i += columns)
            {
                var rowItems = allVisible.Skip(i).Take(columns).ToList();
                GridRows.Add(new DownloadRow { Items = rowItems });
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
                FolderDownloadsView.Refresh();
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

        public DownloadsStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;

            _downloadCacheDir = Path.Combine(Path.GetTempPath(), "RMX3171_DownloadCache");
            Task.Run(() => CleanupCacheDir());

            FolderGroupsView = new ListCollectionView(FolderGroups) { Filter = FilterFolder };
            FolderDownloadsView = new ListCollectionView(FolderDownloads) { Filter = FilterDownload };

            UpdateSorting();
        }

        private void CleanupCacheDir()
        {
            try
            {
                if (Directory.Exists(_downloadCacheDir))
                {
                    foreach (var file in Directory.GetFiles(_downloadCacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllDownloads.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - LastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanDownloadsAsync();
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
            SelectedDownload = null;
            UpdateSelection(Array.Empty<DownloadItem>());
            CurrentBreadcrumb = "Storage > Downloads";
            SearchQuery = "";
            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanDownloadsAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning download metadata...";
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
                    ApplyResults(new List<DownloadItem>(), new List<DownloadFolderGroup>(), 0, 0, false);
                    ScanStatusText = "No downloads found on device.";
                    return;
                }

                var regex = new Regex(@"^Row: \d+ _id=(.*?), _data=(.*?), _size=(\d*), date_modified=(\d*), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*)$", RegexOptions.Compiled);
                var parsedDocs = new List<DownloadItem>();
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
                        
                        bool isDownload = data.IndexOf("/Download/", StringComparison.OrdinalIgnoreCase) >= 0 || data.IndexOf("/Downloads/", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isDownload) continue;

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
                            try { name = Path.GetFileName(data); } catch { name = "Download"; }
                        }

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try { dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime; } catch { }
                        }

                        var (loc, vol) = GetStorageInfo(data);
                        if (loc == StorageLocation.ExternalSd) hasSdCard = true;

                        var doc = new DownloadItem
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

                var folderList = new List<DownloadFolderGroup>();
                if (parsedDocs.Count > 0)
                {
                    folderList.Add(new DownloadFolderGroup
                    {
                        FolderName = "All Discovered Downloads",
                        DownloadCount = parsedDocs.Count,
                        TotalSizeBytes = totalBytes,
                        Location = StorageLocation.Unknown
                    });
                }

                foreach (var kvp in groupDict.Values.OrderByDescending(g => g.bytes))
                {
                    folderList.Add(new DownloadFolderGroup
                    {
                        FolderName = kvp.folder,
                        DownloadCount = kvp.count,
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
                ScanStatusText = $"Downloads scan failed.\nReason: {ex.Message}";
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

        private void ApplyResults(List<DownloadItem> docs, List<DownloadFolderGroup> groups, long totalBytes, int totalCount, bool hasSdCard)
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

                    AllDownloads.Clear();
                    AllDownloads.AddRange(docs);

                    FolderGroups.Clear();
                    foreach (var g in groups) FolderGroups.Add(g);

                    TotalStorageSummaryText = FormatSize(totalBytes);
                    TotalDownloadsCountText = $"{totalCount:N0} downloads";
                    IsLoaded = true;

                    OnTotalDownloadStorageUpdated?.Invoke(totalBytes / (1024.0 * 1024.0 * 1024.0), totalCount);

                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not DownloadFolderGroup group) return false;
            
            if (SelectedStorageFilter == "Internal Storage" && group.Location != StorageLocation.Internal && group.Location != StorageLocation.Unknown) return false;
            if (SelectedStorageFilter == "SD Card" && group.Location != StorageLocation.ExternalSd && group.Location != StorageLocation.Unknown) return false;
            
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterDownload(object obj)
        {
            if (obj is not DownloadItem doc) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return doc.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || doc.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderDownloadsView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.SizeBytes), ListSortDirection.Descending)); break;
                case "Size (Smallest)": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.SizeBytes), ListSortDirection.Ascending)); break;
                case "Newest": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.DateModified), ListSortDirection.Descending)); break;
                case "Oldest": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.DateModified), ListSortDirection.Ascending)); break;
                case "Name": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.FileName), ListSortDirection.Ascending)); break;
                case "Type": FolderDownloadsView.SortDescriptions.Add(new SortDescription(nameof(DownloadItem.Extension), ListSortDirection.Ascending)); break;
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
        private void OpenFolder(DownloadFolderGroup folder)
        {
            if (folder == null) return;
            
            CurrentFolderGroup = folder;
            IsFolderView = false;
            
            SelectedDownload = null;
            UpdateSelection(Array.Empty<DownloadItem>());
            
            string sourcePrefix = folder.LocationLabel;
            if (folder.FolderName == "All Discovered Downloads")
                CurrentBreadcrumb = "Downloads > All Discovered";
            else
                CurrentBreadcrumb = $"Downloads > {sourcePrefix} > {folder.FolderName}";
                
            SelectedFolderSummaryText = $"{folder.DownloadCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            FolderDownloads.Clear();
            IEnumerable<DownloadItem> items = folder.FolderName == "All Discovered Downloads" 
                ? AllDownloads 
                : AllDownloads.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase) && p.Location == folder.Location);

            if (SelectedStorageFilter == "Internal Storage")
                items = items.Where(p => p.Location == StorageLocation.Internal);
            else if (SelectedStorageFilter == "SD Card")
                items = items.Where(p => p.Location == StorageLocation.ExternalSd);

            foreach (var item in items) FolderDownloads.Add(item);

            UpdateSorting();
        }

            }
}
