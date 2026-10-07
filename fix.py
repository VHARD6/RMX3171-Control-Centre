import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\StorageViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add fields
content = content.replace('private readonly DocumentsStorageViewModel _documentsStorageViewModel;', 
'''private readonly DocumentsStorageViewModel _documentsStorageViewModel;
        private readonly DownloadsStorageViewModel _downloadsStorageViewModel;
        private readonly ApksStorageViewModel _apksStorageViewModel;
        private readonly OtherStorageViewModel _otherStorageViewModel;
        private readonly CleanupOpportunitiesViewModel _cleanupOpportunitiesViewModel;''')

# Add to constructor
content = content.replace('DocumentsStorageViewModel documentsStorageViewModel)',
'''DocumentsStorageViewModel documentsStorageViewModel,
            DownloadsStorageViewModel downloadsStorageViewModel,
            ApksStorageViewModel apksStorageViewModel,
            OtherStorageViewModel otherStorageViewModel,
            CleanupOpportunitiesViewModel cleanupOpportunitiesViewModel)''')

# Add assignments
content = content.replace('_documentsStorageViewModel = documentsStorageViewModel;',
'''_documentsStorageViewModel = documentsStorageViewModel;
            _downloadsStorageViewModel = downloadsStorageViewModel;
            _apksStorageViewModel = apksStorageViewModel;
            _otherStorageViewModel = otherStorageViewModel;
            _cleanupOpportunitiesViewModel = cleanupOpportunitiesViewModel;''')

# Add ObservableProperties
content = content.replace('private string _documentStorageTotalText = "Scan required";',
'''private string _documentStorageTotalText = "Scan required";
        [ObservableProperty] private string _downloadStorageTotalText = "Scan required";
        [ObservableProperty] private string _apkStorageTotalText = "Scan required";
        [ObservableProperty] private string _otherStorageTotalText = "Scan required";''')

# Add Event Hooks
hooks = '''
            _downloadsStorageViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
                _downloadsStorageViewModel.OnNavigatedFrom();
            };
            _downloadsStorageViewModel.OnTotalDownloadStorageUpdated += (totalGb, count) =>
            {
                DownloadStorageTotalText = $"{totalGb:F2} GB";
            };

            _apksStorageViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
                _apksStorageViewModel.OnNavigatedFrom();
            };
            _apksStorageViewModel.OnTotalApkStorageUpdated += (totalGb, count) =>
            {
                ApkStorageTotalText = $"{totalGb:F2} GB";
            };

            _otherStorageViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
                _otherStorageViewModel.OnNavigatedFrom();
            };
            _otherStorageViewModel.OnTotalOtherStorageUpdated += (totalGb, count) =>
            {
                OtherStorageTotalText = $"{totalGb:F2} GB";
            };

            _cleanupOpportunitiesViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
            };
'''
content = content.replace('var cached = _telemetryService.CurrentSnapshot?.Storage;', hooks + '\n            var cached = _telemetryService.CurrentSnapshot?.Storage;')

# Add navigation commands
navs = '''
        [RelayCommand]
        private async Task NavigateToDownloadsAsync()
        {
            IsHubVisible = false;
            CurrentView = _downloadsStorageViewModel;
            await _downloadsStorageViewModel.OnNavigatedToAsync();
        }

        [RelayCommand]
        private async Task NavigateToApksAsync()
        {
            IsHubVisible = false;
            CurrentView = _apksStorageViewModel;
            await _apksStorageViewModel.OnNavigatedToAsync();
        }

        [RelayCommand]
        private async Task NavigateToOtherAsync()
        {
            IsHubVisible = false;
            CurrentView = _otherStorageViewModel;
            await _otherStorageViewModel.OnNavigatedToAsync();
        }

        [RelayCommand]
        private async Task NavigateToCleanupAsync()
        {
            IsHubVisible = false;
            CurrentView = _cleanupOpportunitiesViewModel;
            await _cleanupOpportunitiesViewModel.AnalyzeAsync(_photosStorageViewModel, _videosStorageViewModel, _documentsStorageViewModel, _downloadsStorageViewModel, _apksStorageViewModel, _otherStorageViewModel);
        }
'''
content = content.replace('public async Task EnsureLoadedAsync()', navs + '\n        public async Task EnsureLoadedAsync()')

with open(path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
