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
    public class DocumentRow
    {
        public List<DocumentItem> Items { get; set; } = new();
    }

    public partial class DocumentsStorageViewModel : ViewModelBase, IDisposable
    {
        private readonly IAdbService _adbService;

        public event Action? OnBackRequested;
        public event Action<double, int>? OnTotalDocumentStorageUpdated;

        // Models
        public ObservableCollection<DocumentFolderGroup> FolderGroups { get; } = new();
        public ListCollectionView FolderGroupsView { get; }

        public List<DocumentItem> AllDocuments { get; } = new();
        public ObservableCollection<DocumentItem> FolderDocuments { get; } = new();
        public ListCollectionView FolderDocumentsView { get; }

        // Virtualized Grid Rows
        public ObservableCollection<DocumentRow> GridRows { get; } = new();
        private int _lastGridColumns = 1;

        // UI States
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isFolderView = true;
        [ObservableProperty] private bool _isGridView = true;
        [ObservableProperty] private string _scanStatusText = "No document scan performed.";
        [ObservableProperty] private string _currentBreadcrumb = "Storage > Documents";
        [ObservableProperty] private string _totalStorageSummaryText = "Scan required";
        [ObservableProperty] private string _totalDocumentsCountText = "";
        [ObservableProperty] private string _selectedFolderSummaryText = "";
        [ObservableProperty] private DocumentFolderGroup? _currentFolderGroup;

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
        [ObservableProperty] private string _viewerLoadingText = "Preparing document...";
        [ObservableProperty] private DocumentItem? _viewerDocument;
        [ObservableProperty] private string? _viewerLocalPath;

        private CancellationTokenSource? _transferCts;
        private readonly string _documentCacheDir;

        // Selection State
        [ObservableProperty] private DocumentItem? _selectedDocument;
        partial void OnSelectedDocumentChanged(DocumentItem? value) => OnPropertyChanged(nameof(HasSelectedDocument));
        public bool HasSelectedDocument => SelectedDocument != null;

        [ObservableProperty] private string _selectionSummaryText = "0 selected";

        // Selection Handlers (called from Code Behind)
        private DocumentItem? _lastClickedDocument;
        
        public void HandleDocumentClick(DocumentItem document, bool isCtrlPressed, bool isShiftPressed)
        {
            if (document == null) return;

            var allVisible = FolderDocumentsView.Cast<DocumentItem>().ToList();
            if (allVisible.Count == 0) return;

            if (isCtrlPressed)
            {
                document.IsSelected = !document.IsSelected;
                SelectedDocument = document.IsSelected ? document : allVisible.FirstOrDefault(p => p.IsSelected);
                _lastClickedDocument = document;
            }
            else if (isShiftPressed && _lastClickedDocument != null)
            {
                int startIndex = allVisible.IndexOf(_lastClickedDocument);
                int endIndex = allVisible.IndexOf(document);
                
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
                    SelectedDocument = document;
                }
            }
            else
            {
                foreach (var p in allVisible) p.IsSelected = false;
                document.IsSelected = true;
                SelectedDocument = document;
                _lastClickedDocument = document;
            }

            UpdateSelectionSummary();
        }

        public async void HandleDocumentDoubleClick(DocumentItem document)
        {
            if (document == null) return;
            
            // Cleanup previous temp file if any
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

            ViewerDocument = document;
            ViewerVisible = true;
            ViewerIsLoading = true;
            ViewerLoadingText = $"Transferring {document.SizeText}...";
            
            _transferCts = new CancellationTokenSource();
            var token = _transferCts.Token;

            try
            {
                if (!Directory.Exists(_documentCacheDir))
                    Directory.CreateDirectory(_documentCacheDir);
                    
                string ext = Path.GetExtension(document.FileName);
                if (string.IsNullOrEmpty(ext)) ext = document.Extension;
                
                string tempFile = Path.Combine(_documentCacheDir, Guid.NewGuid().ToString("N") + ext);
                
                // Transfer via ADB
                var pullCmd = $"pull \"{document.FullPath}\" \"{tempFile}\"";
                await _adbService.ExecuteCommandAsync(pullCmd, true, token);
                
                token.ThrowIfCancellationRequested();
                
                if (File.Exists(tempFile))
                {
                    ViewerLocalPath = tempFile;
                    ViewerIsLoading = false;
                    
                    // Since it's a document, we just try to open it with the default system application
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = tempFile,
                            UseShellExecute = true
                        });
                        
                        // Close the internal viewer overlay since we just launched external app
                        await CloseViewerAsync();
                    }
                    catch (Exception ex)
                    {
                        ViewerIsLoading = false;
                        ViewerLoadingText = $"Failed to open: {ex.Message}";
                    }
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
            
            // Do NOT delete the temporary file here! 
            // External applications need time to launch and lock the file.
            // CleanupCacheDir() handles garbage collection on the next app launch.
            ViewerLocalPath = null;
            ViewerDocument = null;
            
            await Task.CompletedTask;
        }

        public void UpdateSelection(IEnumerable<DocumentItem> items)
        {
            var allVisible = FolderDocumentsView.Cast<DocumentItem>().ToList();
            foreach (var p in allVisible) p.IsSelected = false;
            
            foreach (var item in items)
            {
                if (item != null) item.IsSelected = true;
            }
            
            SelectedDocument = items.LastOrDefault();
            _lastClickedDocument = SelectedDocument;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            var selected = FolderDocumentsView.Cast<DocumentItem>().Where(p => p.IsSelected).ToList();
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
            
            var allVisible = FolderDocumentsView.Cast<DocumentItem>().ToList();
            GridRows.Clear();
            
            for (int i = 0; i < allVisible.Count; i += columns)
            {
                var rowItems = allVisible.Skip(i).Take(columns).ToList();
                GridRows.Add(new DocumentRow { Items = rowItems });
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
                FolderDocumentsView.Refresh();
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

        public DocumentsStorageViewModel(IAdbService adbService)
        {
            _adbService = adbService;

            _documentCacheDir = Path.Combine(Path.GetTempPath(), "RMX3171_DocumentCache");
            Task.Run(() => CleanupCacheDir());

            FolderGroupsView = new ListCollectionView(FolderGroups) { Filter = FilterFolder };
            FolderDocumentsView = new ListCollectionView(FolderDocuments) { Filter = FilterDocument };

            UpdateSorting();
        }

        private void CleanupCacheDir()
        {
            try
            {
                if (Directory.Exists(_documentCacheDir))
                {
                    foreach (var file in Directory.GetFiles(_documentCacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public async Task OnNavigatedToAsync()
        {
            if (AllDocuments.Count > 0 && !IsScanning)
            {
                var secondsAgo = (DateTime.Now - LastScanTime).TotalSeconds;
                ScanStatusText = $"Last scanned: {secondsAgo:F0} seconds ago";
                IsLoaded = true;
                return;
            }

            if (!IsScanning)
            {
                await ScanDocumentsAsync();
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
            SelectedDocument = null;
            UpdateSelection(Array.Empty<DocumentItem>());
            CurrentBreadcrumb = "Storage > Documents";
            SearchQuery = "";
            UpdateSorting();
        }

        [RelayCommand]
        private async Task ScanDocumentsAsync()
        {
            if (IsScanning) return;

            IsScanning = true;
            IsLoaded = false;
            ScanStatusText = "Scanning document metadata...";
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
                    ApplyResults(new List<DocumentItem>(), new List<DocumentFolderGroup>(), 0, 0, false);
                    ScanStatusText = "No documents found on device.";
                    return;
                }

                var regex = new Regex(@"^Row: \d+ _id=(.*?), _data=(.*?), _size=(\d*), date_modified=(\d*), mime_type=(.*?), bucket_display_name=(.*?), _display_name=(.*)$", RegexOptions.Compiled);
                var parsedDocs = new List<DocumentItem>();
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
                        
                        bool isDoc = ext switch {
                            ".pdf" => true,
                            ".doc" => true, ".docx" => true,
                            ".xls" => true, ".xlsx" => true,
                            ".ppt" => true, ".pptx" => true,
                            ".txt" => true, ".csv" => true, ".rtf" => true,
                            ".epub" => true,
                            ".zip" => true, ".rar" => true, ".7z" => true,
                            _ => false
                        };
                        
                        if (!isDoc) continue;

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
                            try { name = Path.GetFileName(data); } catch { name = "Document"; }
                        }

                        DateTime dateModified = DateTime.MinValue;
                        if (dateSec > 0)
                        {
                            try { dateModified = DateTimeOffset.FromUnixTimeSeconds(dateSec).LocalDateTime; } catch { }
                        }

                        var (loc, vol) = GetStorageInfo(data);
                        if (loc == StorageLocation.ExternalSd) hasSdCard = true;

                        var doc = new DocumentItem
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

                var folderList = new List<DocumentFolderGroup>();
                if (parsedDocs.Count > 0)
                {
                    folderList.Add(new DocumentFolderGroup
                    {
                        FolderName = "All Discovered Documents",
                        DocumentCount = parsedDocs.Count,
                        TotalSizeBytes = totalBytes,
                        Location = StorageLocation.Unknown
                    });
                }

                foreach (var kvp in groupDict.Values.OrderByDescending(g => g.bytes))
                {
                    folderList.Add(new DocumentFolderGroup
                    {
                        FolderName = kvp.folder,
                        DocumentCount = kvp.count,
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
                ScanStatusText = $"Documents scan failed.\nReason: {ex.Message}";
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

        private void ApplyResults(List<DocumentItem> docs, List<DocumentFolderGroup> groups, long totalBytes, int totalCount, bool hasSdCard)
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

                    AllDocuments.Clear();
                    AllDocuments.AddRange(docs);

                    FolderGroups.Clear();
                    foreach (var g in groups) FolderGroups.Add(g);

                    TotalStorageSummaryText = FormatSize(totalBytes);
                    TotalDocumentsCountText = $"{totalCount:N0} documents";
                    IsLoaded = true;

                    OnTotalDocumentStorageUpdated?.Invoke(totalBytes / (1024.0 * 1024.0 * 1024.0), totalCount);

                    if (!IsFolderView && CurrentFolderGroup != null)
                    {
                        OpenFolder(CurrentFolderGroup);
                    }
                });
            }
        }

        private bool FilterFolder(object obj)
        {
            if (obj is not DocumentFolderGroup group) return false;
            
            if (SelectedStorageFilter == "Internal Storage" && group.Location != StorageLocation.Internal && group.Location != StorageLocation.Unknown) return false;
            if (SelectedStorageFilter == "SD Card" && group.Location != StorageLocation.ExternalSd && group.Location != StorageLocation.Unknown) return false;
            
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return group.FolderName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private bool FilterDocument(object obj)
        {
            if (obj is not DocumentItem doc) return false;
            if (string.IsNullOrWhiteSpace(SearchQuery)) return true;
            return doc.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || doc.Folder.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSorting()
        {
            FolderDocumentsView.SortDescriptions.Clear();
            switch (SelectedSort)
            {
                case "Size (Largest)": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.SizeBytes), ListSortDirection.Descending)); break;
                case "Size (Smallest)": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.SizeBytes), ListSortDirection.Ascending)); break;
                case "Newest": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.DateModified), ListSortDirection.Descending)); break;
                case "Oldest": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.DateModified), ListSortDirection.Ascending)); break;
                case "Name": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.FileName), ListSortDirection.Ascending)); break;
                case "Type": FolderDocumentsView.SortDescriptions.Add(new SortDescription(nameof(DocumentItem.Extension), ListSortDirection.Ascending)); break;
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
        private void OpenFolder(DocumentFolderGroup folder)
        {
            if (folder == null) return;
            
            CurrentFolderGroup = folder;
            IsFolderView = false;
            
            SelectedDocument = null;
            UpdateSelection(Array.Empty<DocumentItem>());
            
            string sourcePrefix = folder.LocationLabel;
            if (folder.FolderName == "All Discovered Documents")
                CurrentBreadcrumb = "Documents > All Discovered";
            else
                CurrentBreadcrumb = $"Documents > {sourcePrefix} > {folder.FolderName}";
                
            SelectedFolderSummaryText = $"{folder.DocumentCountText}, {folder.TotalSizeText}";
            SearchQuery = "";

            FolderDocuments.Clear();
            IEnumerable<DocumentItem> items = folder.FolderName == "All Discovered Documents" 
                ? AllDocuments 
                : AllDocuments.Where(p => string.Equals(p.Folder, folder.FolderName, StringComparison.OrdinalIgnoreCase) && p.Location == folder.Location);

            if (SelectedStorageFilter == "Internal Storage")
                items = items.Where(p => p.Location == StorageLocation.Internal);
            else if (SelectedStorageFilter == "SD Card")
                items = items.Where(p => p.Location == StorageLocation.ExternalSd);

            foreach (var item in items) FolderDocuments.Add(item);

            UpdateSorting();
        }

            }
}
