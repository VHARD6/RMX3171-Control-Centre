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
    public class VideoRow
    {
        public List<VideoItem> Items { get; set; } = new();
    }

    public partial class VideosStorageViewModel : ViewModelBase, IDisposable
    {
        private readonly IAdbService _adbService;
        private readonly VideoThumbnailLoader _thumbnailLoader;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalVideoStorageUpdated;
        
        // Models
        public ObservableCollection<VideoFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<VideoItem> AllVideos { get; } = new();
        public ObservableCollection<VideoItem> FolderVideos { get; } = new();
        public ListCollectionView FolderVideosView { get; }

        // Virtualized Grid Rows
        public ObservableCollection<VideoRow> GridRows { get; } = new();
        private int _lastGridColumns = 1;

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private bool _isGridView = true;
        [ObservableProperty] private string _scanStatusText = "No video scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Videos";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalVideosCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private VideoFolderGroup? _currentFolderGroup;

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
        [ObservableProperty] private string _viewerLoadingText = "Preparing video...";
        [ObservableProperty] private VideoItem? _viewerVideo;
        [ObservableProperty] private string? _viewerLocalPath;

        private CancellationTokenSource? _transferCts;
        private readonly string _videoCacheDir;

        // Selection State
        [ObservableProperty] private VideoItem? _selectedVideo;
        partial void OnSelectedVideoChanged(VideoItem? value) => OnPropertyChanged(nameof(HasSelectedVideo));
        public bool HasSelectedVideo => SelectedVideo != null;

        [ObservableProperty] private string _selectionSummaryText = "0 selected";
        
        // Expose Thumbnail Loader to the view for row virtualization
        public VideoThumbnailLoader ThumbnailLoader => _thumbnailLoader;

        // Selection Handlers (called from Code Behind)
        private VideoItem? _lastClickedVideo;
        
        public void HandleVideoClick(VideoItem video, bool isCtrlPressed, bool isShiftPressed)
        {
            if (video == null) return;

            var allVisible = FolderVideosView.Cast<VideoItem>().ToList();
            if (allVisible.Count == 0) return;

            if (isCtrlPressed)
            {
                video.IsSelected = !video.IsSelected;
                SelectedVideo = video.IsSelected ? video : allVisible.FirstOrDefault(p => p.IsSelected);
                _lastClickedVideo = video;
            }
            else if (isShiftPressed && _lastClickedVideo != null)
            {
                int startIndex = allVisible.IndexOf(_lastClickedVideo);
                int endIndex = allVisible.IndexOf(video);
                
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
                    SelectedVideo = video;
                }
            }
            else
            {
                foreach (var p in allVisible) p.IsSelected = false;
                video.IsSelected = true;
                SelectedVideo = video;
                _lastClickedVideo = video;
            }

            UpdateSelectionSummary();
        }

        public async void HandleVideoDoubleClick(VideoItem video)
        {
            if (video == null) return;
            
            // Cleanup previous temp file if any
            if (_transferCts != null)
            {
                _transferCts.Cancel();
                _transferCts.Dispose();
                _transferCts = null;
            }
            
            if (ViewerLocalPath != null)
            {
                string oldPath = ViewerLocalPath;
                ViewerLocalPath = null;
                // Wait for WPF MediaElement to release the file lock
                await Task.Delay(100); 
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
            }

            ViewerVideo = video;
            ViewerVisible = true;
            ViewerIsLoading = true;
            ViewerLoadingText = $"Transferring {video.SizeText}...";
            
            _transferCts = new CancellationTokenSource();
            var token = _transferCts.Token;

            try
            {
                if (!Directory.Exists(_videoCacheDir))
                    Directory.CreateDirectory(_videoCacheDir);
                    
                string ext = Path.GetExtension(video.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".mp4";
                
                string tempFile = Path.Combine(_videoCacheDir, Guid.NewGuid().ToString("N") + ext);
                
                // Transfer via ADB
                var pullCmd = $"pull \"{video.FullPath}\" \"{tempFile}\"";
                await _adbService.ExecuteCommandAsync(pullCmd, true, token);
                
                token.ThrowIfCancellationRequested();
                
                if (File.Exists(tempFile))
                {
                    ViewerLocalPath = tempFile;
                    ViewerIsLoading = false;
                }
                else
                {
                    ViewerLoadingText = "Transfer failed.";
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
                    ViewerLoadingText = $"Error: {ex.Message}";
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
                        
            if (ViewerLocalPath != null)
            {
                string pathToDelete = ViewerLocalPath;
                ViewerLocalPath = null;
                
                // Allow MediaElement to close and release lock
                await Task.Delay(300);
                try
                {
                    if (File.Exists(pathToDelete))
                        File.Delete(pathToDelete);
                }
                catch { }
            }
            
            ViewerVideo = null;
        }

        public void UpdateSelection(IEnumerable<VideoItem> items)
        {
            var allVisible = FolderVideosView.Cast<VideoItem>().ToList();
            foreach (var p in allVisible) p.IsSelected = false;
            
            foreach (var item in items)
            {
                if (item != null) item.IsSelected = true;
            }
            
            SelectedVideo = items.LastOrDefault();
            _lastClickedVideo = SelectedVideo;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = FolderVideosView.Cast<VideoItem>().Where(p => p.IsSelected).ToList();
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
            
            var allVisible = FolderVideosView.Cast<VideoItem>().ToList();
            GridRows.Clear();
            
            for (int i = 0; i < allVisible.Count; i += columns)
            {
                var rowItems = allVisible.Skip(i).Take(columns).ToList();
                GridRows.Add(new VideoRow { Items = rowItems });
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
                FolderVideosView.Refresh();
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
            "Longest",
            "Shortest"
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

        public VideosStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;
            _thumbnailLoader = new VideoThumbnailLoader(_adbService);

            _videoCacheDir = Path.Combine(Path.GetTempPath(), "RMX3171_VideoCache");
            Task.Run(() => CleanupCacheDir());

            FolderGroupsView = new ListCollectionView(FolderGroups) { Filter = FilterFolder };
            FolderVideosView = new ListCollectionView(FolderVideos) { Filter = FilterVideo };

            UpdateSorting();
        }

        private void CleanupCacheDir()
        {
            try
            {
                if (Directory.Exists(_videoCacheDir))
                {
                    foreach (var file in Directory.GetFiles(_videoCacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllVideos.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - LastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanVideosAsync();
            }
        }

        public void OnNavigatedFrom()
        {
            CancelScan();
            _thumbnailLoader.Clear();
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
            SelectedVideo = null;
            UpdateSelection(Array.Empty<VideoItem>());
            CurrentBreadcrumb = "Storage > Videos";
            SearchQuery = "";
            GridRows.Clear();
        }

        [RelayCommand]
        private void OpenFolder(VideoFolderGroup folder)
        {
            if (folder == null) return;

            CurrentFolderGroup = folder;
            IsFolderView = false;
            SelectedVideo = null;
            UpdateSelection(Array.Empty<VideoItem>());
            
            string sourcePrefix = folder.LocationLabel;
            if (folder.FolderName == "All Discovered Videos")
                CurrentBreadcrumb = "Videos > All Discovered";
            else
                CurrentBreadcrumb = $"Videos > {sourcePrefix} > {folder.FolderName}";

            SelectedFolderSummaryText = $"{folder.VideoCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            FolderVideos.Clear();
            IEnumerable<VideoItem> items = folder.FolderName == "All Discovered Videos" 
                ? AllVideos 
                : AllVideos.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase) && p.Location == folder.Location);

            if (SelectedStorageFilter == "Internal Storage")
                items = items.Where(p => p.Location == StorageLocation.Internal);
            else if (SelectedStorageFilter == "SD Card")
                items = items.Where(p => p.Location == StorageLocation.ExternalSd);

            foreach (var item in items) FolderVideos.Add(item);

            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanVideosAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning video metadata...";
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            
            try
            {
                const string queryCmd = "shell \"content query --uri content://media/external/video/media --projection _id:_data:_size:date_modified:mime_type:bucket_display_name:_display_name:duration:resolution\"";
                var queryResult = await _adbService.ExecuteCommandAsync(queryCmd, isReadOnly: true, token);
                token.ThrowIfCancellationRequested();

                if (queryResult.ExitCode != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(queryResult.Output) ? "Unable to enumerate this location." : queryResult.Output.Trim());

                if (string.IsNullOrWhiteSpace(queryResult.Output) || queryResult.Output.Contains("No result found"))
                {
                    ApplyResults(new List<VideoItem>(), new List<VideoFolderGroup>(), 0, 0, false);
                    ScanStatusText = "No videos found on device.";
                    return;
                }

                // Row: 0 _id=39, _data=/storage/FCE2-B64E/video.mp4, _size=49086671, date_modified=1595433524, mime_type=video/mp4, bucket_display_name=NULL, _display_name=video.mp4, duration=25613, resolution=1088x1920
                var regex = new Regex(@"^Row: \d+ _id=(.*?), _data=(.*?), _size=(\d+), date_modified=(\d+), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*?), duration=(\d*), resolution=(.*)$", RegexOptions.Compiled);
                var parsedVideos = new List<VideoItem>();
                var groupDict = new Dictionary<string, (int count, long bytes, string samplePath, StorageLocation loc, string folder)>(StringComparer.OrdinalIgnoreCase);

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

                        string id = match.Groups[1].Value.Trim();
                        string data = match.Groups[2].Value.Trim();
                        long size = long.TryParse(match.Groups[3].Value, out long s) ? s : 0;
                        long dateSec = long.TryParse(match.Groups[4].Value, out long d) ? d : 0;
                        string mime = match.Groups[5].Value.Trim();
                        string bucket = match.Groups[6].Value.Trim();
                        string name = match.Groups[7].Value.Trim();
                        long duration = long.TryParse(match.Groups[8].Value, out long ms) ? ms : 0;
                        string resolution = match.Groups[9].Value.Trim();

                        if (string.IsNullOrWhiteSpace(bucket) || bucket.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { bucket = Path.GetFileName(Path.GetDirectoryName(data)) ?? "Other"; } catch { bucket = "Other"; }
                        }
                        if (string.IsNullOrWhiteSpace(name) || name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { name = Path.GetFileName(data); } catch { name = "Video"; }
                        }
                        if (resolution.Equals("NULL", StringComparison.OrdinalIgnoreCase)) resolution = "Unknown";

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try { dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime; } catch { }
                        }

                        var (loc, vol) = GetStorageInfo(data);
                        if (loc == StorageLocation.ExternalSd) hasSdCard = true;

                        var video = new VideoItem
                        {
                            Id = id,
                            FileName = name,
                            FullPath = data,
                            Folder = bucket,
                            SizeBytes = size,
                            DateModified = dateModified,
                            MimeType = mime.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : mime,
                            DurationMs = duration,
                            Resolution = resolution,
                            Location = loc,
                            VolumeId = vol
                        };

                        parsedVideos.Add(video);
                        totalBytes += size;

                        string groupKey = $"{bucket}|{loc}";
                        if (groupDict.TryGetValue(groupKey, out var existing))
                            groupDict[groupKey] = (existing.count + 1, existing.bytes + size, existing.samplePath, loc, bucket);
                        else
                            groupDict[groupKey] = (1, size, data, loc, bucket);
                    }
                }

                var folderList = new List<VideoFolderGroup>();
                if (parsedVideos.Count > 0)
                {
                    folderList.Add(new VideoFolderGroup
                    {
                        FolderName = "All Discovered Videos",
                        VideoCount = parsedVideos.Count,
                        TotalSizeBytes = totalBytes,
                        SamplePath = "All device storage locations",
                        Location = StorageLocation.Unknown
                    });
                }

                foreach (var kvp in groupDict.Values.OrderByDescending(g => g.bytes))
                {
                    folderList.Add(new VideoFolderGroup
                    {
                        FolderName = kvp.folder,
                        VideoCount = kvp.count,
                        TotalSizeBytes = kvp.bytes,
                        SamplePath = kvp.samplePath,
                        Location = kvp.loc
                    });
                }

                ApplyResults(parsedVideos, folderList, totalBytes, parsedVideos.Count, hasSdCard);

                LastScanTime = DateTime.Now;
                ScanStatusText = "Last scanned: 0 seconds ago";
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Videos scan failed.\nReason: {ex.Message}";
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

        private void ApplyResults(List<VideoItem> videos, List<VideoFolderGroup> groups, long totalBytes, int totalCount, bool hasSdCard)
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

                    AllVideos.Clear();
                    AllVideos.AddRange(videos);

                    FolderGroups.Clear();
                    foreach (var g in groups) FolderGroups.Add(g);

                    TotalStorageSummaryText = FormatSize(totalBytes);
                    TotalVideosCountText = $"{totalCount:N0} videos";
                    IsLoaded = true;

                    OnTotalVideoStorageUpdated?.Invoke(totalBytes / (1024.0 * 1024.0 * 1024.0), totalCount);

                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not VideoFolderGroup group) return false;
            
            if (SelectedStorageFilter == "Internal Storage" && group.Location != StorageLocation.Internal && group.Location != StorageLocation.Unknown) return false;
            if (SelectedStorageFilter == "SD Card" && group.Location != StorageLocation.ExternalSd && group.Location != StorageLocation.Unknown) return false;
            
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || group.SamplePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterVideo(object obj)
        {
            if (obj is not VideoItem video) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return video.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || video.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderVideosView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.SizeBytes), ListSortDirection.Descending)); break;
                case "Size (Smallest)": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.SizeBytes), ListSortDirection.Ascending)); break;
                case "Newest": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.DateModified), ListSortDirection.Descending)); break;
                case "Oldest": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.DateModified), ListSortDirection.Ascending)); break;
                case "Name": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.FileName), ListSortDirection.Ascending)); break;
                case "Longest": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.DurationMs), ListSortDirection.Descending)); break;
                case "Shortest": FolderVideosView.SortDescriptions.Add(new SortDescription(nameof(VideoItem.DurationMs), ListSortDirection.Ascending)); break;
            }
            if (!IsFolderView)
            {
                UpdateGridColumns(_lastGridColumns);
            }
        }

        public DateTime LastScanTime { get; private set; } = DateTime.MinValue;

        public void Dispose()
        {
            _thumbnailLoader?.Dispose();
            _scanCts?.Dispose();
        }
    }
}
