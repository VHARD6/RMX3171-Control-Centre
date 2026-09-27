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
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.ViewModels.Storage
{
    public class PhotoRow
    {
        public List<PhotoItem> Items { get; set; } = new();
    }

    public partial class PhotosStorageViewModel : ViewModelBase, IDisposable
    {
        private readonly IAdbService _adbService;
        private readonly ThumbnailLoader _thumbnailLoader;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalPhotoStorageUpdated;

        // Models
        public ObservableCollection<PhotoFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<PhotoItem> AllPhotos { get; } = new();
        public ObservableCollection<PhotoItem> FolderPhotos { get; } = new();
        public ListCollectionView FolderPhotosView { get; }

        // Virtualized Grid Rows
        public ObservableCollection<PhotoRow> GridRows { get; } = new();
        private int _lastGridColumns = 1;

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private bool _isGridView = true;
        [ObservableProperty] private string _scanStatusText = "No photo scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Photos";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalPhotosCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private PhotoFolderGroup? _currentFolderGroup;

        // Photo Viewer State
        [ObservableProperty] private bool _viewerVisible;
        [ObservableProperty] private PhotoItem? _viewerPhoto;
        [ObservableProperty] private BitmapImage? _viewerImageSource;
        [ObservableProperty] private bool _viewerIsLoading;
        [ObservableProperty] private double _viewerScale = 0; // 0 = Fit to window
        private CancellationTokenSource? _viewerLoadCts;
        private CancellationTokenSource? _scanCts;

        // View Mode Toggles
        [RelayCommand] private void ShowGridView() => IsGridView = true;
        [RelayCommand] private void ShowListView() => IsGridView = false;

        // Selection State
        [ObservableProperty] private PhotoItem? _selectedPhoto;
        partial void OnSelectedPhotoChanged(PhotoItem? value) => OnPropertyChanged(nameof(HasSelectedPhoto));
        public bool HasSelectedPhoto => SelectedPhoto != null;

        [ObservableProperty] private string _selectionSummaryText = "0 selected";
        
        // Expose Thumbnail Loader to the view for row virtualization
        public ThumbnailLoader ThumbnailLoader => _thumbnailLoader;

        // Selection Handlers (called from Code Behind)
        private PhotoItem? _lastClickedPhoto;
        
        public void HandlePhotoClick(PhotoItem photo, bool isCtrlPressed, bool isShiftPressed)
        {
            if (photo == null) return;

            var allVisible = FolderPhotosView.Cast<PhotoItem>().ToList();
            if (allVisible.Count == 0) return;

            if (isCtrlPressed)
            {
                photo.IsSelected = !photo.IsSelected;
                SelectedPhoto = photo.IsSelected ? photo : allVisible.FirstOrDefault(p => p.IsSelected);
                _lastClickedPhoto = photo;
            }
            else if (isShiftPressed && _lastClickedPhoto != null)
            {
                int startIndex = allVisible.IndexOf(_lastClickedPhoto);
                int endIndex = allVisible.IndexOf(photo);
                
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
                    SelectedPhoto = photo;
                }
            }
            else
            {
                foreach (var p in allVisible) p.IsSelected = false;
                photo.IsSelected = true;
                SelectedPhoto = photo;
                _lastClickedPhoto = photo;
            }

            UpdateSelectionSummary();
        }

        public void HandlePhotoDoubleClick(PhotoItem photo)
        {
            if (photo != null)
            {
                OpenViewer(photo);
            }
        }

        public void UpdateSelection(IEnumerable<PhotoItem> items)
        {
            // Update models based on list/datagrid selection (in List view)
            var allVisible = FolderPhotosView.Cast<PhotoItem>().ToList();
            foreach (var p in allVisible) p.IsSelected = false;
            
            foreach (var item in items)
            {
                if (item != null) item.IsSelected = true;
            }
            
            SelectedPhoto = items.LastOrDefault();
            _lastClickedPhoto = SelectedPhoto;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = FolderPhotosView.Cast<PhotoItem>().Where(p => p.IsSelected).ToList();
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
            
            var allVisible = FolderPhotosView.Cast<PhotoItem>().ToList();
            GridRows.Clear();
            
            for (int i = 0; i < allVisible.Count; i += columns)
            {
                var rowItems = allVisible.Skip(i).Take(columns).ToList();
                GridRows.Add(new PhotoRow { Items = rowItems });
            }
        }

        // --- VIEWER COMMANDS ---

        private async void OpenViewer(PhotoItem photo)
        {
            ViewerVisible = true;
            ViewerPhoto = photo;
            ViewerScale = 0; // Fit to window
            ViewerImageSource = null;
            ViewerIsLoading = true;

            _viewerLoadCts?.Cancel();
            _viewerLoadCts = new CancellationTokenSource();
            var token = _viewerLoadCts.Token;

            try
            {
                var bmp = await _thumbnailLoader.GetFullImageAsync(photo, token);
                if (!token.IsCancellationRequested)
                {
                    ViewerImageSource = bmp; // null acts as placeholder if failed
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                    ViewerIsLoading = false;
            }
        }

        [RelayCommand]
        private void CloseViewer()
        {
            _viewerLoadCts?.Cancel();
            ViewerVisible = false;
            ViewerImageSource = null; // Free memory!
            ViewerPhoto = null;
        }

        public event Action? OnFitRequested;
        public event Action? OnActualSizeRequested;
        public event Action? OnZoomInRequested;
        public event Action? OnZoomOutRequested;

        [RelayCommand] private void ZoomInViewer() => OnZoomInRequested?.Invoke();
        [RelayCommand] private void ZoomOutViewer() => OnZoomOutRequested?.Invoke();
        [RelayCommand] private void FitViewer() => OnFitRequested?.Invoke();
        [RelayCommand] private void ActualSizeViewer() => OnActualSizeRequested?.Invoke();

        [RelayCommand]
        private void NextViewerPhoto()
        {
            var all = FolderPhotosView.Cast<PhotoItem>().ToList();
            if (ViewerPhoto == null || all.Count == 0) return;
            
            int idx = all.IndexOf(ViewerPhoto);
            if (idx >= 0 && idx < all.Count - 1)
            {
                OpenViewer(all[idx + 1]);
            }
        }

        [RelayCommand]
        private void PrevViewerPhoto()
        {
            var all = FolderPhotosView.Cast<PhotoItem>().ToList();
            if (ViewerPhoto == null || all.Count == 0) return;
            
            int idx = all.IndexOf(ViewerPhoto);
            if (idx > 0)
            {
                OpenViewer(all[idx - 1]);
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
                FolderPhotosView.Refresh();
                UpdateGridColumns(_lastGridColumns); // Regroup after filter
            }
        }

        // Sort Options
        public ObservableCollection<string> SortOptions { get; } = new(new[]
        {
            "Newest",
            "Oldest",
            "Size (Largest)",
            "Size (Smallest)",
            "Name"
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

        public PhotosStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;
            _thumbnailLoader = new ThumbnailLoader(_adbService);

            FolderGroupsView = new ListCollectionView(FolderGroups) { Filter = FilterFolder };
            FolderPhotosView = new ListCollectionView(FolderPhotos) { Filter = FilterPhoto };

            UpdateSorting();
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllPhotos.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - LastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanPhotosAsync();
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
            SelectedPhoto = null;
            UpdateSelection(Array.Empty<PhotoItem>());
            CurrentBreadcrumb = "Storage > Photos";
            SearchQuery = "";
            GridRows.Clear();
        }

        [RelayCommand]
        private void OpenFolder(PhotoFolderGroup folder)
        {
            if (folder == null) return;

            CurrentFolderGroup = folder;
            IsFolderView = false;
            SelectedPhoto = null;
            UpdateSelection(Array.Empty<PhotoItem>());
            
            string sourcePrefix = folder.LocationLabel;
            if (folder.FolderName == "All Discovered Photos")
                CurrentBreadcrumb = "Photos > All Discovered";
            else
                CurrentBreadcrumb = $"Photos > {sourcePrefix} > {folder.FolderName}";

            SelectedFolderSummaryText = $"{folder.PhotoCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            FolderPhotos.Clear();
            IEnumerable<PhotoItem> items = folder.FolderName == "All Discovered Photos" 
                ? AllPhotos 
                : AllPhotos.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase) && p.Location == folder.Location);

            if (SelectedStorageFilter == "Internal Storage")
                items = items.Where(p => p.Location == StorageLocation.Internal);
            else if (SelectedStorageFilter == "SD Card")
                items = items.Where(p => p.Location == StorageLocation.ExternalSd);

            foreach (var item in items) FolderPhotos.Add(item);

            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanPhotosAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning photo metadata...";
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;
            
            try
            {
                const string queryCmd = "shell \"content query --uri content://media/external/images/media --projection _data:_size:date_modified:mime_type:bucket_display_name:_display_name\"";
                var queryResult = await _adbService.ExecuteCommandAsync(queryCmd, isReadOnly: true, token);
                token.ThrowIfCancellationRequested();

                if (queryResult.ExitCode != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(queryResult.Output) ? "Unable to enumerate this location." : queryResult.Output.Trim());

                if (string.IsNullOrWhiteSpace(queryResult.Output) || queryResult.Output.Contains("No result found"))
                {
                    ApplyResults(new List<PhotoItem>(), new List<PhotoFolderGroup>(), 0, 0, false);
                    ScanStatusText = "No photos found on device.";
                    return;
                }

                var regex = new Regex(@"^Row: \d+ _data=(.*?), _size=(\d+), date_modified=(\d+), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*)$", RegexOptions.Compiled);
                var parsedPhotos = new List<PhotoItem>();
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

                        string data = match.Groups[1].Value.Trim();
                        long size = long.TryParse(match.Groups[2].Value, out long s) ? s : 0;
                        long dateSec = long.TryParse(match.Groups[3].Value, out long d) ? d : 0;
                        string mime = match.Groups[4].Value.Trim();
                        string bucket = match.Groups[5].Value.Trim();
                        string name = match.Groups[6].Value.Trim();

                        if (string.IsNullOrWhiteSpace(bucket) || bucket.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { bucket = Path.GetFileName(Path.GetDirectoryName(data)) ?? "Other"; } catch { bucket = "Other"; }
                        }
                        if (string.IsNullOrWhiteSpace(name) || name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try { name = Path.GetFileName(data); } catch { name = "Photo"; }
                        }

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try { dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime; } catch { }
                        }

                        string ext = "";
                        try { ext = Path.GetExtension(name); } catch { }

                        var (loc, vol) = GetStorageInfo(data);
                        if (loc == StorageLocation.ExternalSd) hasSdCard = true;

                        var photo = new PhotoItem
                        {
                            FileName = name,
                            FullPath = data,
                            Folder = bucket,
                            SizeBytes = size,
                            DateModified = dateModified,
                            Extension = ext,
                            MimeType = mime.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : mime,
                            Location = loc,
                            VolumeId = vol
                        };

                        parsedPhotos.Add(photo);
                        totalBytes += size;

                        string groupKey = $"{bucket}|{loc}";
                        if (groupDict.TryGetValue(groupKey, out var existing))
                            groupDict[groupKey] = (existing.count + 1, existing.bytes + size, existing.samplePath, loc, bucket);
                        else
                            groupDict[groupKey] = (1, size, data, loc, bucket);
                    }
                }

                var folderList = new List<PhotoFolderGroup>();
                if (parsedPhotos.Count > 0)
                {
                    folderList.Add(new PhotoFolderGroup
                    {
                        FolderName = "All Discovered Photos",
                        PhotoCount = parsedPhotos.Count,
                        TotalSizeBytes = totalBytes,
                        SamplePath = "All device storage locations",
                        Location = StorageLocation.Unknown
                    });
                }

                foreach (var kvp in groupDict.Values.OrderByDescending(g => g.bytes))
                {
                    folderList.Add(new PhotoFolderGroup
                    {
                        FolderName = kvp.folder,
                        PhotoCount = kvp.count,
                        TotalSizeBytes = kvp.bytes,
                        SamplePath = kvp.samplePath,
                        Location = kvp.loc
                    });
                }

                ApplyResults(parsedPhotos, folderList, totalBytes, parsedPhotos.Count, hasSdCard);

                LastScanTime = DateTime.Now;
                ScanStatusText = "Last scanned: 0 seconds ago";
            }
            catch (OperationCanceledException)
            {
                ScanStatusText = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                ScanStatusText = $"Photos scan failed.\nReason: {ex.Message}";
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

        private void ApplyResults(List<PhotoItem> photos, List<PhotoFolderGroup> groups, long totalBytes, int totalCount, bool hasSdCard)
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

                    AllPhotos.Clear();
                    AllPhotos.AddRange(photos);

                    FolderGroups.Clear();
                    foreach (var g in groups) FolderGroups.Add(g);

                    TotalStorageSummaryText = FormatSize(totalBytes);
                    TotalPhotosCountText = $"{totalCount:N0} photos";
                    IsLoaded = true;

                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }
                    OnTotalPhotoStorageUpdated?.Invoke(totalBytes / (1024.0 * 1024.0 * 1024.0), totalCount);
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not PhotoFolderGroup group) return false;
            
            if (SelectedStorageFilter == "Internal Storage" && group.Location != StorageLocation.Internal && group.Location != StorageLocation.Unknown) return false;
            if (SelectedStorageFilter == "SD Card" && group.Location != StorageLocation.ExternalSd && group.Location != StorageLocation.Unknown) return false;
            
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || group.SamplePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterPhoto(object obj)
        {
            if (obj is not PhotoItem photo) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return photo.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || photo.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderPhotosView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)": FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.SizeBytes), ListSortDirection.Descending)); break;
                case "Size (Smallest)": FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.SizeBytes), ListSortDirection.Ascending)); break;
                case "Newest": FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.DateModified), ListSortDirection.Descending)); break;
                case "Oldest": FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.DateModified), ListSortDirection.Ascending)); break;
                case "Name": FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.FileName), ListSortDirection.Ascending)); break;
            }
            if (!IsFolderView)
            {
                UpdateGridColumns(_lastGridColumns);
            }
        }

        public DateTime LastScanTime { get; private set; } = DateTime.MinValue;

        public void Dispose()
        {
            _viewerLoadCts?.Dispose();
            _thumbnailLoader?.Dispose();
            _scanCts?.Dispose();
        }
    }
}
