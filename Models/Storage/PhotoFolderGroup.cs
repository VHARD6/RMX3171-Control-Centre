namespace RMX3171ControlCentre.Models.Storage
{
    public class PhotoFolderGroup
    {
        public string FolderName { get; set; } = string.Empty;
        public int PhotoCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string SamplePath { get; set; } = string.Empty;

        public string PhotoCountText => PhotoCount == 1 ? "1 photo" : $"{PhotoCount:N0} photos";

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
