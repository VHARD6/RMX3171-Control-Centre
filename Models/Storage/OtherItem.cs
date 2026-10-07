using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models.Storage
{
    public partial class OtherItem : ObservableObject
    {
        [ObservableProperty] private string _id = string.Empty;
        [ObservableProperty] private string _fileName = string.Empty;
        [ObservableProperty] private string _fullPath = string.Empty;
        [ObservableProperty] private string _folder = string.Empty;
        [ObservableProperty] private long _sizeBytes;
        [ObservableProperty] private DateTime _dateModified = DateTime.MinValue;
        [ObservableProperty] private string _mimeType = string.Empty;
        [ObservableProperty] private string _extension = string.Empty;

        [ObservableProperty] private StorageLocation _location = StorageLocation.Unknown;
        [ObservableProperty] private string _volumeId = string.Empty;
        [ObservableProperty] private bool _isSelected;

        public string StorageSourceLabel => Location == StorageLocation.Internal ? "INTERNAL" : 
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
