using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models.Storage
{
    public partial class StorageInsightCategory : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private string _colorHex = "#FF555555";
        [ObservableProperty] private double _percentageOfTotal;
        [ObservableProperty] private double _percentageOfUsed;

        public string SizeText
        {
            get
            {
                if (SizeBytes >= 1024L * 1024L * 1024L)
                    return $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                if (SizeBytes >= 1024L * 1024L)
                    return $"{SizeBytes / (1024.0 * 1024.0):F1} MB";
                if (SizeBytes >= 1024L)
                    return $"{SizeBytes / 1024.0:F0} KB";
                return $"{SizeBytes} B";
            }
        }

        public string PercentageOfTotalText => $"{PercentageOfTotal:F1}% of Total";
        public string PercentageOfUsedText => $"{PercentageOfUsed:F1}% of Used";
    }
}
