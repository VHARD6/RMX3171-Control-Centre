using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;
using RMX3171ControlCentre.Services.Adb;


namespace RMX3171ControlCentre.ViewModels.Storage
{
    public partial class CleanupOpportunitiesViewModel : ViewModelBase
    {
        public event Action? OnBackRequested;
        
        private readonly IAppModeService _appModeService;
        private readonly IDialogService _dialogService;
        private readonly IAuditService _auditService;
        private readonly IAdbService _adbService;

        public CleanupOpportunitiesViewModel(
            IAppModeService appModeService,
            IDialogService dialogService,
            IAuditService auditService,
            IAdbService adbService)
        {
            _appModeService = appModeService;
            _dialogService = dialogService;
            _auditService = auditService;
            _adbService = adbService;
            
            _appModeService.ModeChanged += (s, e) => 
            {
                OnPropertyChanged(nameof(CanDelete));
            };
        }

        public bool CanDelete => _appModeService.CurrentMode == RMX3171ControlCentre.Models.AppMode.Advanced || 
                                 _appModeService.CurrentMode == RMX3171ControlCentre.Models.AppMode.Expert;

        public event Action<ViewModelBase>? RequestViewChange;
        
        private PhotosStorageViewModel? _photosVm;
        private VideosStorageViewModel? _videosVm;
        private DocumentsStorageViewModel? _docsVm;
        private DownloadsStorageViewModel? _downVm;
        private ApksStorageViewModel? _apksVm;
        private OtherStorageViewModel? _otherVm;
        
        private bool _isPreviewActive;


        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private string _analysisStatusText = "Ready to analyze.";
        
                public ObservableCollection<CleanupCandidate> Candidates { get; } = new();

        public ObservableCollection<string> SortOptions { get; } = new(new[]
        {
            "Size — Largest first",
            "Size — Smallest first",
            "Modified — Newest first",
            "Modified — Oldest first",
            "Name — A → Z",
            "Name — Z → A",
            "Category",
            "Risk / Recommendation"
        });

        [ObservableProperty] private string _selectedSort = "Size — Largest first";

        partial void OnSelectedSortChanged(string value)
        {
            UpdateSorting();
        }

        private void UpdateSorting()
        {
            if (Candidates.Count == 0) return;
            
            var list = Candidates.ToList();
            list = SelectedSort switch
            {
                "Size — Largest first" => list.OrderByDescending(x => x.SizeBytes).ToList(),
                "Size — Smallest first" => list.OrderBy(x => x.SizeBytes).ToList(),
                "Modified — Newest first" => list.OrderByDescending(x => x.DateModified).ToList(),
                "Modified — Oldest first" => list.OrderBy(x => x.DateModified).ToList(),
                "Name — A → Z" => list.OrderBy(x => x.FileName).ToList(),
                "Name — Z → A" => list.OrderByDescending(x => x.FileName).ToList(),
                "Category" => list.OrderBy(x => x.Category).ThenByDescending(x => x.SizeBytes).ToList(),
                "Risk / Recommendation" => list.OrderBy(x => x.RecommendationLevel).ThenByDescending(x => x.SizeBytes).ToList(),
                _ => list
            };
            
            Candidates.Clear();
            foreach (var c in list)
            {
                Candidates.Add(c);
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            OnBackRequested?.Invoke();
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        [RelayCommand]
        private async Task DeleteSelectedAsync()
        {
            if (!CanDelete)
            {
                _dialogService.ShowMessage("Access Denied", "Deletion requires Advanced Mode.");
                return;
            }

            var selected = Candidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                _dialogService.ShowMessage("No Selection", "Please select items to delete.");
                return;
            }

            long totalBytes = selected.Sum(x => x.SizeBytes);
            string formattedTotal = FormatSize(totalBytes);
            
            // Build summary string for the dialog
            string targetList = selected.Count == 1 
                ? $"1 item: {selected[0].FileName}" 
                : $"{selected.Count} items (Total: {formattedTotal})";
                
            string stateList = string.Join("\n", selected.Select(x => $"- {x.FullPath} ({x.SizeText})"));
            
            // Check for unsafe paths
            var unsafePaths = new[] { "/system", "/vendor", "/product", "/data/system", "/sbin" };
            foreach (var item in selected)
            {
                if (unsafePaths.Any(p => item.FullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _dialogService.ShowMessage("Safety Violation", $"Cannot delete protected system path:\n{item.FullPath}");
                    return;
                }
            }

            // Verify with audit UI
            var firstItem = selected[0];
            string escapedFirstPath = firstItem.FullPath.Replace("'", "'\\''");
            string adbCmd = selected.Count == 1 
                ? $"shell rm '{escapedFirstPath}'" 
                : $"shell rm ... ({selected.Count} items)";

            bool confirmed = await _dialogService.ShowModificationPreviewAsync(
                "Delete Files",
                targetList,
                stateList,
                "Delete permanently from storage",
                adbCmd,
                "HIGH (Irreversible)",
                "Files will be completely removed. This action cannot be undone."
            );

            if (!confirmed) return;

            IsAnalyzing = true;
            AnalysisStatusText = $"Deleting {selected.Count} items...";

            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var item in selected.ToList())
                {
                    // Using AdbService directly with isReadOnly: false
                    string escapedItemPath = item.FullPath.Replace("'", "'\\''");
                    var cmd = $"shell rm '{escapedItemPath}'";
                    var result = await _adbService.ExecuteCommandAsync(cmd, false);
                    
                    if (result.ExitCode == 0 && !result.Error.Contains("Permission denied") && !result.Error.Contains("No such file"))
                    {
                        successCount++;
                        _auditService.LogModification("Delete", item.FullPath, "Exists", "Deleted", cmd, true);
                        
                        // Remove from Candidates UI
                        Candidates.Remove(item);
                        
                        // Optionally remove from the underlying VMs if present
                        if (_photosVm != null) _photosVm.AllPhotos.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_videosVm != null) _videosVm.AllVideos.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_docsVm != null) _docsVm.AllDocuments.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_downVm != null) _downVm.AllDownloads.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_apksVm != null) _apksVm.AllApks.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_otherVm != null) _otherVm.AllOtherItems.RemoveAll(x => x.FullPath == item.FullPath);
                    }
                    else
                    {
                        failCount++;
                        _auditService.LogModification("Delete", item.FullPath, "Exists", "Failed", cmd, false);
                        System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                            _dialogService.ShowMessage("Deletion Failed", $"Failed to delete {item.FileName}.\nError: {result.Error}\nOutput: {result.Output}");
                        });
                    }
                }
                
                AnalysisStatusText = $"Deletion complete. {successCount} succeeded, {failCount} failed.";
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Deletion error: {ex.Message}";
                _dialogService.ShowMessage("Error", ex.Message);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }


        [RelayCommand]
        private async Task PreviewAsync(CleanupCandidate candidate)
        {
            if (candidate == null) return;
            try
            {
                _isPreviewActive = true;
                AnalysisStatusText = "Opening preview...";

                if (candidate.Category.Contains("Photo") && _photosVm != null)
                {
                    RequestViewChange?.Invoke(_photosVm);
                    var item = _photosVm.AllPhotos.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _photosVm.HandlePhotoDoubleClick(item);
                }
                else if (candidate.Category.Contains("Video") && _videosVm != null)
                {
                    RequestViewChange?.Invoke(_videosVm);
                    var item = _videosVm.AllVideos.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _videosVm.HandleVideoDoubleClick(item);
                }
                else if (candidate.Category.Contains("Document") && _docsVm != null)
                {
                    RequestViewChange?.Invoke(_docsVm);
                    var item = _docsVm.AllDocuments.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _docsVm.HandleDocumentDoubleClick(item);
                }
                else if (candidate.Category.Contains("Download") && _downVm != null)
                {
                    RequestViewChange?.Invoke(_downVm);
                    var item = _downVm.AllDownloads.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _downVm.HandleDownloadDoubleClick(item);
                }
                else if (candidate.Category.Contains("APK") && _apksVm != null)
                {
                    RequestViewChange?.Invoke(_apksVm);
                    var item = _apksVm.AllApks.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _apksVm.HandleApkDoubleClick(item);
                }
                else if (candidate.Category.Contains("Other") && _otherVm != null)
                {
                    RequestViewChange?.Invoke(_otherVm);
                    var item = _otherVm.AllOtherItems.FirstOrDefault(x => x.FullPath == candidate.FullPath);
                    if (item != null) _otherVm.HandleOtherDoubleClick(item);
                }
                else
                {
                    // Fallback for folders or unsupported
                    System.Windows.MessageBox.Show($"Preview not available for {candidate.Category}.", "Preview", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    _isPreviewActive = false;
                }
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Preview failed: {ex.Message}";
                _isPreviewActive = false;
            }
            finally
            {
                AnalysisStatusText = $"Found {Candidates.Count} cleanup opportunities.";
            }
        }

        private void OnChildViewerClosed()
        {
            if (_isPreviewActive)
            {
                _isPreviewActive = false;
                RequestViewChange?.Invoke(this);
            }
        }


        public async Task AnalyzeAsync(
            PhotosStorageViewModel photosVm,
            VideosStorageViewModel videosVm,
            DocumentsStorageViewModel docsVm,
            DownloadsStorageViewModel downVm,
            ApksStorageViewModel apksVm,
            OtherStorageViewModel otherVm)
        {
            if (IsAnalyzing) return;
            IsAnalyzing = true;
            IsLoaded = false;

            // Unsubscribe previous if they exist
            if (_photosVm != null) _photosVm.OnViewerClosed -= OnChildViewerClosed;
            if (_videosVm != null) _videosVm.OnViewerClosed -= OnChildViewerClosed;
            if (_docsVm != null) _docsVm.OnViewerClosed -= OnChildViewerClosed;
            if (_downVm != null) _downVm.OnViewerClosed -= OnChildViewerClosed;
            if (_apksVm != null) _apksVm.OnViewerClosed -= OnChildViewerClosed;
            if (_otherVm != null) _otherVm.OnViewerClosed -= OnChildViewerClosed;

            _photosVm = photosVm;
            _videosVm = videosVm;
            _docsVm = docsVm;
            _downVm = downVm;
            _apksVm = apksVm;
            _otherVm = otherVm;
            
            _photosVm.OnViewerClosed += OnChildViewerClosed;
            _videosVm.OnViewerClosed += OnChildViewerClosed;
            _docsVm.OnViewerClosed += OnChildViewerClosed;
            _downVm.OnViewerClosed += OnChildViewerClosed;
            _apksVm.OnViewerClosed += OnChildViewerClosed;
            _otherVm.OnViewerClosed += OnChildViewerClosed;

            
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
                dispatcher.Invoke(() => Candidates.Clear());

            try
            {
                AnalysisStatusText = "Scanning Photos...";
                if (!photosVm.IsLoaded) await photosVm.OnNavigatedToAsync();
                
                AnalysisStatusText = "Scanning Videos...";
                if (!videosVm.IsLoaded) await videosVm.OnNavigatedToAsync();
                
                AnalysisStatusText = "Scanning Documents...";
                if (!docsVm.IsLoaded) await docsVm.OnNavigatedToAsync();
                
                AnalysisStatusText = "Scanning Downloads...";
                if (!downVm.IsLoaded) await downVm.OnNavigatedToAsync();
                
                AnalysisStatusText = "Scanning APKs...";
                if (!apksVm.IsLoaded) await apksVm.OnNavigatedToAsync();
                
                AnalysisStatusText = "Scanning Others...";
                if (!otherVm.IsLoaded) await otherVm.OnNavigatedToAsync();

                AnalysisStatusText = "Compiling cleanup opportunities...";
                
                var newCandidates = new List<CleanupCandidate>();

                // Rule 1: Large Videos (> 200MB)
                foreach (var v in videosVm.AllVideos.Where(x => x.SizeBytes > 200 * 1024L * 1024L))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Large Video",
                        FileName = v.FileName,
                        FullPath = v.FullPath,
                        SizeBytes = v.SizeBytes,
                        DateModified = v.DateModified,
                        Location = v.Location,
                        Reason = "Video is unusually large (>200MB).",
                        RecommendationLevel = "Review"
                    });
                }

                // Rule 2: Large Photos (> 20MB)
                foreach (var p in photosVm.AllPhotos.Where(x => x.SizeBytes > 20 * 1024L * 1024L))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Large Photo",
                        FileName = p.FileName,
                        FullPath = p.FullPath,
                        SizeBytes = p.SizeBytes,
                        DateModified = p.DateModified,
                        Location = p.Location,
                        Reason = "Photo is unusually large (>20MB).",
                        RecommendationLevel = "Likely safe to review"
                    });
                }

                // Rule 3: Old/Unused APKs (> 90 days)
                var apkThreshold = DateTime.Now.AddDays(-90);
                foreach (var a in apksVm.AllApks.Where(x => x.DateModified > DateTime.MinValue && x.DateModified < apkThreshold))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Old APK",
                        FileName = a.FileName,
                        FullPath = a.FullPath,
                        SizeBytes = a.SizeBytes,
                        DateModified = a.DateModified,
                        Location = a.Location,
                        Reason = "APK is older than 90 days.",
                        RecommendationLevel = "Likely safe to review"
                    });
                }

                // Rule 4: Large Downloads (> 100MB)
                foreach (var d in downVm.AllDownloads.Where(x => x.SizeBytes > 100 * 1024L * 1024L))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Large Download",
                        FileName = d.FileName,
                        FullPath = d.FullPath,
                        SizeBytes = d.SizeBytes,
                        DateModified = d.DateModified,
                        Location = d.Location,
                        Reason = "Download file is very large (>100MB).",
                        RecommendationLevel = "Review"
                    });
                }
                
                // Rule 5: Old Downloads (> 180 days)
                var oldThreshold = DateTime.Now.AddDays(-180);
                foreach (var d in downVm.AllDownloads.Where(x => x.SizeBytes > 0 && x.SizeBytes < 100 * 1024L * 1024L && x.DateModified > DateTime.MinValue && x.DateModified < oldThreshold))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Old Download",
                        FileName = d.FileName,
                        FullPath = d.FullPath,
                        SizeBytes = d.SizeBytes,
                        DateModified = d.DateModified,
                        Location = d.Location,
                        Reason = "Downloaded over 180 days ago.",
                        RecommendationLevel = "Likely safe to review"
                    });
                }

                // Rule 6: Large Other Files (> 200MB)
                foreach (var o in otherVm.AllOtherItems.Where(x => x.SizeBytes > 200 * 1024L * 1024L))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Large Other File",
                        FileName = o.FileName,
                        FullPath = o.FullPath,
                        SizeBytes = o.SizeBytes,
                        DateModified = o.DateModified,
                        Location = o.Location,
                        Reason = "Uncategorized file is very large (>200MB).",
                        RecommendationLevel = "Potentially important"
                    });
                }

                // Rule 7: Large Documents (> 50MB)
                foreach (var d in docsVm.AllDocuments.Where(x => x.SizeBytes > 50 * 1024L * 1024L))
                {
                    newCandidates.Add(new CleanupCandidate {
                        Category = "Large Document",
                        FileName = d.FileName,
                        FullPath = d.FullPath,
                        SizeBytes = d.SizeBytes,
                        DateModified = d.DateModified,
                        Location = d.Location,
                        Reason = "Document is unusually large (>50MB).",
                        RecommendationLevel = "Review"
                    });
                }
                
                // Rule 8: Duplicates (Same Size and Name)
                var allFiles = new List<(string Name, long Size, string Path, string Category, DateTime Date, StorageLocation Loc)>();
                allFiles.AddRange(photosVm.AllPhotos.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate Photo", x.DateModified, x.Location)));
                allFiles.AddRange(videosVm.AllVideos.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate Video", x.DateModified, x.Location)));
                allFiles.AddRange(docsVm.AllDocuments.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate Document", x.DateModified, x.Location)));
                allFiles.AddRange(downVm.AllDownloads.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate Download", x.DateModified, x.Location)));
                allFiles.AddRange(apksVm.AllApks.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate APK", x.DateModified, x.Location)));
                allFiles.AddRange(otherVm.AllOtherItems.Select(x => (x.FileName, x.SizeBytes, x.FullPath, "Duplicate Other File", x.DateModified, x.Location)));
                
                var duplicates = allFiles
                    .GroupBy(x => new { x.Name, x.Size })
                    .Where(g => g.Count() > 1 && g.Key.Size > 1024)
                    .SelectMany(g => g.Skip(1))
                    .ToList();
                    
                foreach (var dup in duplicates)
                {
                    if (!newCandidates.Any(c => c.FullPath == dup.Path))
                    {
                        newCandidates.Add(new CleanupCandidate {
                            Category = dup.Category,
                            FileName = dup.Name,
                            FullPath = dup.Path,
                            SizeBytes = dup.Size,
                            DateModified = dup.Date,
                            Location = dup.Loc,
                            Reason = "File appears to be a duplicate (same name & size).",
                            RecommendationLevel = "Likely safe to review"
                        });
                    }
                }

                // Sort by size descending
                newCandidates = newCandidates.OrderByDescending(x => x.SizeBytes).ToList(); // Default initial sort

                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                    {
                        foreach (var c in newCandidates)
                        {
                            Candidates.Add(c);
                        }
                    });
                }
                
                AnalysisStatusText = $"Found {Candidates.Count} cleanup opportunities.";
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Analysis failed: {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }
    }
}
