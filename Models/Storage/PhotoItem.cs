using System;

namespace RMX3171ControlCentre.Models.Storage
{
    public class PhotoItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime DateModified { get; set; } = DateTime.MinValue;
        public string Extension { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;

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
