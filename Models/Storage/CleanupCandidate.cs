using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models.Storage
{
    public partial class CleanupCandidate : ObservableObject
    {
        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private string _category = string.Empty;
        [ObservableProperty] private string _fileName = string.Empty;
        [ObservableProperty] private string _fullPath = string.Empty;
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private DateTime _dateModified = DateTime.MinValue;
        [ObservableProperty] private string _reason = string.Empty;
        [ObservableProperty] private string _recommendationLevel = string.Empty;
        [ObservableProperty] private StorageLocation _location = StorageLocation.Unknown;

        public string LocationLabel => Location == StorageLocation.Internal ? "INTERNAL" : 
                                       Location == StorageLocation.ExternalSd ? "SD CARD" : "UNKNOWN";

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

        public string DateModifiedText => DateModified == DateTime.MinValue
            ? "Unknown"
            : DateModified.ToString("yyyy-MM-dd HH:mm");
    }
}
