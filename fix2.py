import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\StorageViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('private readonly CleanupOpportunitiesViewModel _cleanupOpportunitiesViewModel;', 
'''private readonly CleanupOpportunitiesViewModel _cleanupOpportunitiesViewModel;
        private readonly StorageInsightsViewModel _storageInsightsViewModel;''')

content = content.replace('CleanupOpportunitiesViewModel cleanupOpportunitiesViewModel)',
'''CleanupOpportunitiesViewModel cleanupOpportunitiesViewModel,
            StorageInsightsViewModel storageInsightsViewModel)''')

content = content.replace('_cleanupOpportunitiesViewModel = cleanupOpportunitiesViewModel;',
'''_cleanupOpportunitiesViewModel = cleanupOpportunitiesViewModel;
            _storageInsightsViewModel = storageInsightsViewModel;''')

hooks = '''
            _storageInsightsViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
            };
'''
content = content.replace('_cleanupOpportunitiesViewModel.OnBackRequested += () =>\n            {\n                IsHubVisible = true;\n                CurrentView = null;\n            };', '_cleanupOpportunitiesViewModel.OnBackRequested += () =>\n            {\n                IsHubVisible = true;\n                CurrentView = null;\n            };\n' + hooks)

navs = '''
        [RelayCommand]
        private async Task NavigateToInsightsAsync()
        {
            IsHubVisible = false;
            CurrentView = _storageInsightsViewModel;
            
            var snap = _telemetryService.CurrentSnapshot?.Storage;
            double totalGb = snap != null ? snap.TotalGb : 0;
            double usedGb = snap != null ? snap.UsedGb : 0;
            double freeGb = snap != null ? snap.FreeGb : 0;
            
            await _storageInsightsViewModel.AnalyzeAsync(totalGb, usedGb, freeGb, _appStorageViewModel, _photosStorageViewModel, _videosStorageViewModel, _documentsStorageViewModel, _downloadsStorageViewModel, _apksStorageViewModel, _otherStorageViewModel);
        }
'''
content = content.replace('public async Task EnsureLoadedAsync()', navs + '\n        public async Task EnsureLoadedAsync()')

with open(path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
