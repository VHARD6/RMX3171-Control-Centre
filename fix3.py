import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\Storage\CleanupOpportunitiesViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add fields
fields = '''
        public event Action<ViewModelBase>? RequestViewChange;
        
        private PhotosStorageViewModel? _photosVm;
        private VideosStorageViewModel? _videosVm;
        private DocumentsStorageViewModel? _docsVm;
        private DownloadsStorageViewModel? _downVm;
        private ApksStorageViewModel? _apksVm;
        private OtherStorageViewModel? _otherVm;
        
        private bool _isPreviewActive;
'''
content = content.replace('public event Action? OnBackRequested;', 'public event Action? OnBackRequested;' + fields)

# Add PreviewCommand
preview_cmd = '''
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
'''
content = content.replace('private void GoBack()\n        {\n            OnBackRequested?.Invoke();\n        }', 'private void GoBack()\n        {\n            OnBackRequested?.Invoke();\n        }\n' + preview_cmd)

# Inject saving references in AnalyzeAsync
saving = '''
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
'''
content = content.replace('IsLoaded = false;', 'IsLoaded = false;\n' + saving)

with open(path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
