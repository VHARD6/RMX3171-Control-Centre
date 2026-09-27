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
    public partial class PhotosStorageViewModel : ViewModelBase
    {
        private readonly IAdbService _adbService;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalPhotoStorageUpdated;

        // Models
        public ObservableCollection<PhotoFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<PhotoItem> AllPhotos { get; } = new();
        public ObservableCollection<PhotoItem> FolderPhotos { get; } = new();
        public ListCollectionView FolderPhotosView { get; }

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private string _scanStatusText = "No photo scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Photos";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalPhotosCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private PhotoFolderGroup? _currentFolderGroup;

        // App Details Selection
        [ObservableProperty] private PhotoItem? _selectedPhoto;
        partial void OnSelectedPhotoChanged(PhotoItem? value) => OnPropertyChanged(nameof(HasSelectedPhoto));
        public bool HasSelectedPhoto => SelectedPhoto != null;

        // Search
        [ObservableProperty] private string _searchQuery = "";
        partial void OnSearchQueryChanged(string value)
        {
            if (IsFolderView)
            {
                FolderGroupsView.Refresh();
            }
            else
            {
                FolderPhotosView.Refresh();
            }
        }

        // Sort Options for Photo Listing
        public ObservableCollection<string> SortOptions { get; } = new(new[]
        {
            "Newest",
            "Oldest",
            "Size (Largest)",
            "Size (Smallest)",
            "Name"
        });

        [ObservableProperty] private string _selectedSort = "Newest";
        partial void OnSelectedSortChanged(string value) => UpdateSorting();

        // Metadata & Diagnostics
        public string DataSource { get; private set; } = "Android MediaStore (content://media/external/images/media)";
        public DateTime LastScanTime { get; private set; } = DateTime.MinValue;
        public long TotalPhotoBytes { get; private set; }
        public int TotalPhotoCount { get; private set; }
        public long ScanDurationMs { get; private set; }

        private CancellationTokenSource? _scanCts;

        public PhotosStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;

            FolderGroupsView = new ListCollectionView(FolderGroups)
            {
                Filter = FilterFolder
            };

            FolderPhotosView = new ListCollectionView(FolderPhotos)
            {
                Filter = FilterPhoto
            };

            UpdateSorting();
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllPhotos.Count > 0 && !IsScanning)
            {
                // Cached data exists, display immediately
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
            CurrentBreadcrumb = "Storage > Photos";
            SearchQuery = "";
        }

        [RelayCommand]
        private void OpenFolder(PhotoFolderGroup folder)
        {
            if (folder == null) return;

            CurrentFolderGroup = folder;
            IsFolderView = false;
            SelectedPhoto = null;
            CurrentBreadcrumb = $"Storage > Photos > {folder.FolderName}";
            SelectedFolderSummaryText = $"{folder.PhotoCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            // Populate FolderPhotos
            FolderPhotos.Clear();
            IEnumerable<PhotoItem> items;
            if (folder.FolderName == "All Discovered Photos")
            {
                items = AllPhotos;
            }
            else
            {
                items = AllPhotos.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in items)
            {
                FolderPhotos.Add(item);
            }

            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanPhotosAsync()
        {
            if (IsScanning) return; // Prevent concurrent scans

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning photos...";
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            var sw = Stopwatch.StartNew();

            try
            {
                // Query Android MediaStore for all external image items
                const string queryCmd = "shell \"content query --uri content://media/external/images/media --projection _data:_size:date_modified:mime_type:bucket_display_name:_display_name\"";
                var queryResult = await _adbService.ExecuteCommandAsync(queryCmd, isReadOnly: true, token);

                token.ThrowIfCancellationRequested();

                if (queryResult.ExitCode != 0)
                {
                    if (queryResult.Output.Contains("SecurityException") || queryResult.Output.Contains("Permission Denial"))
                    {
                        throw new UnauthorizedAccessException("Access restricted by Android security policy.");
                    }
                    throw new Exception(string.IsNullOrWhiteSpace(queryResult.Output)
                        ? "Unable to enumerate this location."
                        : queryResult.Output.Trim());
                }

                if (string.IsNullOrWhiteSpace(queryResult.Output) || queryResult.Output.Contains("No result found"))
                {
                    // No photos found
                    ApplyResults(new List<PhotoItem>(), new List<PhotoFolderGroup>(), 0, 0);
                    ScanStatusText = "No photos found on device.";
                    return;
                }

                // Line-by-line parsing to avoid large regex object graphs
                var regex = new Regex(@"^Row: \d+ _data=(.*?), _size=(\d+), date_modified=(\d+), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*)$", RegexOptions.Compiled);

                var parsedPhotos = new List<PhotoItem>();
                var groupDict = new Dictionary<string, (int count, long bytes, string samplePath)>(StringComparer.OrdinalIgnoreCase);

                long totalBytes = 0;

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
                            try
                            {
                                bucket = Path.GetFileName(Path.GetDirectoryName(data)) ?? "Other";
                            }
                            catch
                            {
                                bucket = "Other";
                            }
                        }

                        if (string.IsNullOrWhiteSpace(name) || name.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                name = Path.GetFileName(data);
                            }
                            catch
                            {
                                name = "Photo";
                            }
                        }

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try
                            {
                                dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime;
                            }
                            catch
                            {
                                dateModified = DateTime.MinValue;
                            }
                        }

                        string ext = "";
                        try
                        {
                            ext = Path.GetExtension(name);
                        }
                        catch { }

                        var photo = new PhotoItem
                        {
                            FileName = name,
                            FullPath = data,
                            Folder = bucket,
                            SizeBytes = size,
                            DateModified = dateModified,
                            Extension = ext,
                            MimeType = mime.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : mime
                        };

                        parsedPhotos.Add(photo);
                        totalBytes += size;

                        if (groupDict.TryGetValue(bucket, out var existing))
                        {
                            groupDict[bucket] = (existing.count + 1, existing.bytes + size, existing.samplePath);
                        }
                        else
                        {
                            groupDict[bucket] = (1, size, data);
                        }
                    }
                }

                // Build discovered folder groups
                var folderList = new List<PhotoFolderGroup>();

                // Add "All Discovered Photos" summary card if we have photos
                if (parsedPhotos.Count > 0)
                {
                    folderList.Add(new PhotoFolderGroup
                    {
                        FolderName = "All Discovered Photos",
                        PhotoCount = parsedPhotos.Count,
                        TotalSizeBytes = totalBytes,
                        SamplePath = "All device storage locations"
                    });
                }

                foreach (var kvp in groupDict.OrderByDescending(g => g.Value.bytes))
                {
                    folderList.Add(new PhotoFolderGroup
                    {
                        FolderName = kvp.Key,
                        PhotoCount = kvp.Value.count,
                        TotalSizeBytes = kvp.Value.bytes,
                        SamplePath = kvp.Value.samplePath
                    });
                }

                ApplyResults(parsedPhotos, folderList, totalBytes, parsedPhotos.Count);

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
                sw.Stop();
                ScanDurationMs = sw.ElapsedMilliseconds;
                IsScanning = false;
                _scanCts?.Dispose();
                _scanCts = null;
            }
        }

        private void ApplyResults(List<PhotoItem> photos, List<PhotoFolderGroup> groups, long totalBytes, int totalCount)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.Invoke(() =>
                {
                    AllPhotos.Clear();
                    AllPhotos.AddRange(photos);

                    FolderGroups.Clear();
                    foreach (var g in groups)
                    {
                        FolderGroups.Add(g);
                    }

                    TotalPhotoBytes = totalBytes;
                    TotalPhotoCount = totalCount;

                    double totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                    TotalStorageSummaryText = $"{totalGb:F2} GB";
                    TotalPhotosCountText = $"{totalCount:N0} photos";

                    IsLoaded = true;

                    // If currently inside a folder, refresh that folder's contents
                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }

                    OnTotalPhotoStorageUpdated?.Invoke(totalGb, totalCount);
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not PhotoFolderGroup group) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;

            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || group.SamplePath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterPhoto(object obj)
        {
            if (obj is not PhotoItem photo) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;

            return photo.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || photo.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
                || photo.FullPath.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderPhotosView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)":
                    FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.SizeBytes), ListSortDirection.Descending));
                    break;
                case "Size (Smallest)":
                    FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.SizeBytes), ListSortDirection.Ascending));
                    break;
                case "Newest":
                    FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.DateModified), ListSortDirection.Descending));
                    break;
                case "Oldest":
                    FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.DateModified), ListSortDirection.Ascending));
                    break;
                case "Name":
                    FolderPhotosView.SortDescriptions.Add(new SortDescription(nameof(PhotoItem.FileName), ListSortDirection.Ascending));
                    break;
            }
        }
    }
}
