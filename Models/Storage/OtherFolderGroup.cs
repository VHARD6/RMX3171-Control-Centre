namespace RMX3171ControlCentre.Models.Storage
{
    public class OtherFolderGroup
    {
        public string FolderName { get; set; } = string.Empty;
        public int OtherCount { get; set; }
        public long TotalSizeBytes { get; set; }
        
        public StorageLocation Location { get; set; } = StorageLocation.Unknown;
        public string VolumeId { get; set; } = string.Empty;

        public string LocationLabel => Location == StorageLocation.Internal ? "Internal Storage" : 
                                       Location == StorageLocation.ExternalSd ? "SD Card" : "Unknown";

        public string OtherCountText => OtherCount == 1 ? "1 file" : $"{OtherCount:N0} files";

        public string TotalSizeText
        {
            get
            {
                if (TotalSizeBytes >= 1024L * 1024L * 1024L)
                    return $"{TotalSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                if (TotalSizeBytes >= 1024L * 1024L)
                    return $"{TotalSizeBytes / (1024.0 * 1024.0):F1} MB";
                if (TotalSizeBytes >= 1024L)
                    return $"{TotalSizeBytes / 1024.0:F0} KB";
                return $"{TotalSizeBytes} B";
            }
        }
    }
}
