using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models.Storage;

namespace RMX3171ControlCentre.ViewModels.Storage
{
    public partial class StorageInsightsViewModel : ViewModelBase
    {
        public event Action? OnBackRequested;

        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private bool _isLoaded;
        [ObservableProperty] private string _analysisStatusText = "Ready to analyze.";

        [ObservableProperty] private string _totalStorageText = "";
        [ObservableProperty] private string _usedStorageText = "";
        [ObservableProperty] private string _freeStorageText = "";
        [ObservableProperty] private string _usagePercentText = "";

        [ObservableProperty] private ObservableCollection<string> _highlights = new();
        
        public ObservableCollection<StorageInsightCategory> Categories { get; } = new();

        [RelayCommand]
        private void GoBack()
        {
            OnBackRequested?.Invoke();
        }

        public async Task AnalyzeAsync(
            double totalGb, double usedGb, double freeGb,
            AppStorageViewModel appVm,
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
            
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.Invoke(() => 
                {
                    Categories.Clear();
                    Highlights.Clear();
                });
            }

            try
            {
                TotalStorageText = $"{totalGb:F1} GB";
                UsedStorageText = $"{usedGb:F1} GB";
                FreeStorageText = $"{freeGb:F1} GB";
                double percent = totalGb > 0 ? (usedGb / totalGb) * 100.0 : 0;
                UsagePercentText = $"{percent:F1}%";

                AnalysisStatusText = "Fetching Apps...";
                if (!appVm.IsLoaded) await appVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching Photos...";
                if (!photosVm.IsLoaded) await photosVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching Videos...";
                if (!videosVm.IsLoaded) await videosVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching Documents...";
                if (!docsVm.IsLoaded) await docsVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching Downloads...";
                if (!downVm.IsLoaded) await downVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching APKs...";
                if (!apksVm.IsLoaded) await apksVm.OnNavigatedToAsync();
                AnalysisStatusText = "Fetching Others...";
                if (!otherVm.IsLoaded) await otherVm.OnNavigatedToAsync();

                AnalysisStatusText = "Compiling insights...";

                long appBytes = (long)appVm.AllApps.Sum(x => x.TotalMb * 1024.0 * 1024.0);
                long photoBytes = photosVm.AllPhotos.Sum(x => x.SizeBytes);
                long videoBytes = videosVm.AllVideos.Sum(x => x.SizeBytes);
                long docBytes = docsVm.AllDocuments.Sum(x => x.SizeBytes);
                long downBytes = downVm.AllDownloads.Sum(x => x.SizeBytes);
                long apkBytes = apksVm.AllApks.Sum(x => x.SizeBytes);
                long otherBytes = otherVm.AllOtherItems.Sum(x => x.SizeBytes);

                long totalBytesCalculated = appBytes + photoBytes + videoBytes + docBytes + downBytes + apkBytes + otherBytes;
                
                // Note: The total actual device used space usually exceeds what we can discover (system files, etc.)
                // But we will use the snapshot totalGb/usedGb for percentages, converting them to bytes for math.
                long actualUsedBytes = (long)(usedGb * 1024L * 1024L * 1024L);
                long actualTotalBytes = (long)(totalGb * 1024L * 1024L * 1024L);

                if (actualUsedBytes == 0) actualUsedBytes = 1;
                if (actualTotalBytes == 0) actualTotalBytes = 1;

                var list = new List<StorageInsightCategory>
                {
                    new StorageInsightCategory { Name = "Apps", SizeBytes = appBytes, ColorHex = "#FF4CAF50" },
                    new StorageInsightCategory { Name = "Photos", SizeBytes = photoBytes, ColorHex = "#FF2196F3" },
                    new StorageInsightCategory { Name = "Videos", SizeBytes = videoBytes, ColorHex = "#FF9C27B0" },
                    new StorageInsightCategory { Name = "Documents", SizeBytes = docBytes, ColorHex = "#FFFF9800" },
                    new StorageInsightCategory { Name = "Downloads", SizeBytes = downBytes, ColorHex = "#FF00BCD4" },
                    new StorageInsightCategory { Name = "APKs", SizeBytes = apkBytes, ColorHex = "#FF8BC34A" },
                    new StorageInsightCategory { Name = "Other", SizeBytes = otherBytes, ColorHex = "#FF9E9E9E" }
                };

                // Free space bar visualization? We just do categories relative to user data.
                foreach (var cat in list)
                {
                    cat.PercentageOfUsed = (double)cat.SizeBytes / actualUsedBytes * 100.0;
                    cat.PercentageOfTotal = (double)cat.SizeBytes / actualTotalBytes * 100.0;
                }

                // Sort largest to smallest
                list = list.OrderByDescending(x => x.SizeBytes).ToList();

                var localHighlights = new List<string>();

                var largest = list.FirstOrDefault();
                if (largest != null && largest.SizeBytes > 0)
                {
                    localHighlights.Add($"The largest category is {largest.Name} ({largest.SizeText}), taking up {largest.PercentageOfTotalText}.");
                }

                if (freeGb < 5.0)
                {
                    localHighlights.Add("Very low free space detected (under 5 GB). Consider reviewing Cleanup Opportunities.");
                }

                var hugeCategory = list.FirstOrDefault(x => x.PercentageOfTotal > 30.0);
                if (hugeCategory != null)
                {
                    localHighlights.Add($"{hugeCategory.Name} occupies an unusually large portion of your device's total storage (>30%).");
                }

                if (localHighlights.Count == 0)
                {
                    localHighlights.Add("Storage appears well-balanced with no critical issues detected.");
                }

                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                    {
                        foreach (var c in list) Categories.Add(c);
                        foreach (var h in localHighlights) Highlights.Add(h);
                    });
                }
                
                AnalysisStatusText = $"Analysis complete.";
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
