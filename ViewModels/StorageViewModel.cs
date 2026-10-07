﻿﻿﻿using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Telemetry;
using RMX3171ControlCentre.ViewModels.Storage;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class StorageViewModel : ViewModelBase
    {
        private readonly ITelemetryService _telemetryService;

        [ObservableProperty] private string _totalGbText = "0.0 GB";
        [ObservableProperty] private string _usedGbText = "0.0 GB";
        [ObservableProperty] private string _freeGbText = "0.0 GB";
        [ObservableProperty] private string _usagePercentText = "0.0 %";
        [ObservableProperty] private double _usagePercent = 0.0;
        [ObservableProperty] private string _internalSummary = "0.0 GB / 0.0 GB used";

        [ObservableProperty] private string _healthIndicatorText = "";
        [ObservableProperty] private string _healthIndicatorColor = "Gray";

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private bool _isUnavailable;
        [ObservableProperty] private string _loadingStatusText = "Loading storage information...";

        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Waiting for data...";

        [ObservableProperty] private bool _isHubVisible = true;
        [ObservableProperty] private ViewModelBase? _currentView;
        [ObservableProperty] private string _appStorageTotalText = "Scan required";
        [ObservableProperty] private string _photoStorageTotalText = "Scan required";
        [ObservableProperty] private string _videoStorageTotalText = "Scan required";
        [ObservableProperty] private string _documentStorageTotalText = "Scan required";
        [ObservableProperty] private string _downloadStorageTotalText = "Scan required";
        [ObservableProperty] private string _apkStorageTotalText = "Scan required";
        [ObservableProperty] private string _otherStorageTotalText = "Scan required";

        private readonly AppStorageViewModel _appStorageViewModel;
        private readonly PhotosStorageViewModel _photosStorageViewModel;
        private readonly VideosStorageViewModel _videosStorageViewModel;
        private readonly DocumentsStorageViewModel _documentsStorageViewModel;
        private readonly DownloadsStorageViewModel _downloadsStorageViewModel;
        private readonly ApksStorageViewModel _apksStorageViewModel;
        private readonly OtherStorageViewModel _otherStorageViewModel;
        private readonly CleanupOpportunitiesViewModel _cleanupOpportunitiesViewModel;
        private readonly StorageInsightsViewModel _storageInsightsViewModel;

        public StorageViewModel(
            ITelemetryService telemetryService,
            AppStorageViewModel appStorageViewModel,
            PhotosStorageViewModel photosStorageViewModel,
            VideosStorageViewModel videosStorageViewModel,
            DocumentsStorageViewModel documentsStorageViewModel,
            DownloadsStorageViewModel downloadsStorageViewModel,
            ApksStorageViewModel apksStorageViewModel,
            OtherStorageViewModel otherStorageViewModel,
            CleanupOpportunitiesViewModel cleanupOpportunitiesViewModel,
            StorageInsightsViewModel storageInsightsViewModel)
        {
            _telemetryService = telemetryService;
            _appStorageViewModel = appStorageViewModel;
            _photosStorageViewModel = photosStorageViewModel;
            _videosStorageViewModel = videosStorageViewModel;
            _documentsStorageViewModel = documentsStorageViewModel;
            _downloadsStorageViewModel = downloadsStorageViewModel;
            _apksStorageViewModel = apksStorageViewModel;
            _otherStorageViewModel = otherStorageViewModel;
            _cleanupOpportunitiesViewModel = cleanupOpportunitiesViewModel;
            _storageInsightsViewModel = storageInsightsViewModel;

            _documentsStorageViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
                _documentsStorageViewModel.OnNavigatedFrom();
            };

            _documentsStorageViewModel.OnTotalDocumentStorageUpdated += (totalGb, count) =>
            {
                DocumentStorageTotalText = $"{totalGb:F2} GB";
            };

            _appStorageViewModel.OnBackRequested += () => 
            {
                IsHubVisible = true;
                CurrentView = null;
                _appStorageViewModel.OnNavigatedFrom();
            };

            _appStorageViewModel.OnTotalAppStorageUpdated += (totalGb) =>
            {
                AppStorageTotalText = $"{totalGb:F1} GB";
            };

            _photosStorageViewModel.OnBackRequested += () => 
            {
                IsHubVisible = true;
                CurrentView = null;
                _photosStorageViewModel.OnNavigatedFrom();
            };

            _photosStorageViewModel.OnTotalPhotoStorageUpdated += (totalGb, count) =>
            {
                PhotoStorageTotalText = $"{totalGb:F2} GB";
            };

            _videosStorageViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
                _videosStorageViewModel.OnNavigatedFrom();
            };

            _videosStorageViewModel.OnTotalVideoStorageUpdated += (totalGb, count) =>
            {
                VideoStorageTotalText = $"{totalGb:F2} GB";
            };

            
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

            _cleanupOpportunitiesViewModel.RequestViewChange += (vm) =>
            {
                IsHubVisible = false;
                CurrentView = vm;
            };


            _storageInsightsViewModel.OnBackRequested += () =>
            {
                IsHubVisible = true;
                CurrentView = null;
            };


            var cached = _telemetryService.CurrentSnapshot?.Storage;
            if (cached != null && cached.TotalGb > 0 && cached.LastUpdated != DateTime.MinValue)
            {
                ApplySnapshot(cached);
            }
            else
            {
                IsLoading = true;
                IsLoaded = false;
                IsUnavailable = false;
                LoadingStatusText = "Loading storage information...";
            }

            _telemetryService.SnapshotUpdated += OnSnapshotUpdated;
        }

        [RelayCommand]
        private async Task NavigateToAppsAsync()
        {
            IsHubVisible = false;
            CurrentView = _appStorageViewModel;
            await _appStorageViewModel.OnNavigatedToAsync();
        }

        [RelayCommand]
        private async Task NavigateToPhotosAsync()
        {
            IsHubVisible = false;
            CurrentView = _photosStorageViewModel;
            await _photosStorageViewModel.OnNavigatedToAsync();
        }

        [RelayCommand]
        private async Task NavigateToVideosAsync()
        {
            IsHubVisible = false;
            CurrentView = _videosStorageViewModel;
            await _videosStorageViewModel.OnNavigatedToAsync();
        }

                [RelayCommand]
        private async Task NavigateToDocumentsAsync()
        {
            IsHubVisible = false;
            CurrentView = _documentsStorageViewModel;
            await _documentsStorageViewModel.OnNavigatedToAsync();
        }

        
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

        public async Task EnsureLoadedAsync()
        {
            var snap = _telemetryService.CurrentSnapshot?.Storage;
            if (snap != null && snap.TotalGb > 0 && snap.LastUpdated != DateTime.MinValue)
            {
                ApplySnapshot(snap);
                return;
            }

            // No cached data exists: show loading and perform ONE read-only query
            IsLoading = true;
            IsLoaded = false;
            IsUnavailable = false;
            LoadingStatusText = "Loading storage information...";

            try
            {
                await _telemetryService.ForceRefreshAsync("Storage");
                var updatedSnap = _telemetryService.CurrentSnapshot?.Storage;
                if (updatedSnap != null && updatedSnap.TotalGb > 0)
                {
                    ApplySnapshot(updatedSnap);
                }
                else
                {
                    IsLoading = false;
                    IsLoaded = false;
                    IsUnavailable = true;
                    LoadingStatusText = _telemetryService.CurrentSnapshot?.IsConnected == true
                        ? "Storage information unavailable."
                        : "Device not connected. Connect via USB or Wireless ADB.";
                }
            }
            catch (Exception ex)
            {
                IsLoading = false;
                IsLoaded = false;
                IsUnavailable = true;
                LoadingStatusText = $"Unable to load storage: {ex.Message}";
            }
        }

        private void OnSnapshotUpdated(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                var snap = _telemetryService.CurrentSnapshot?.Storage;
                if (snap != null && snap.TotalGb > 0)
                {
                    ApplySnapshot(snap);
                }
                else if (_telemetryService.CurrentSnapshot?.IsConnected != true)
                {
                    if (IsLoading)
                    {
                        IsLoading = false;
                        IsLoaded = false;
                        IsUnavailable = true;
                        LoadingStatusText = "Device not connected. Connect via USB or Wireless ADB.";
                    }
                }
            }));
        }

        private void ApplySnapshot(StorageState snap)
        {
            TotalGbText = $"{snap.TotalGb:F1} GB";
            UsedGbText = $"{snap.UsedGb:F1} GB";
            FreeGbText = $"{snap.FreeGb:F1} GB";
            UsagePercentText = $"{snap.UsagePercentage:F1} %";
            UsagePercent = snap.UsagePercentage;
            InternalSummary = $"{snap.UsedGb:F1} GB / {snap.TotalGb:F1} GB used";

            if (snap.UsagePercentage < 70)
            {
                HealthIndicatorText = "Healthy free space";
                HealthIndicatorColor = "LimeGreen";
            }
            else if (snap.UsagePercentage < 85)
            {
                HealthIndicatorText = "Getting full";
                HealthIndicatorColor = "Gold";
            }
            else if (snap.UsagePercentage < 95)
            {
                HealthIndicatorText = "Low free space";
                HealthIndicatorColor = "Orange";
            }
            else
            {
                HealthIndicatorText = "Very low free space";
                HealthIndicatorColor = "Red";
            }

            var secondsAgo = (DateTime.Now - snap.LastUpdated).TotalSeconds;
            LastUpdatedText = secondsAgo > 20 ? $"Last updated {secondsAgo:F0}s ago" : "Live";

            IsLoading = false;
            IsLoaded = true;
            IsUnavailable = false;
        }

        [RelayCommand]
        private async Task RefreshStorageAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                await _telemetryService.ForceRefreshAsync("All");
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}


