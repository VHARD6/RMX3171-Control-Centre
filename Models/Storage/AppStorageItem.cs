using System;

namespace RMX3171ControlCentre.Models.Storage
{
    public class AppStorageItem
    {
        public string Application { get; set; } = string.Empty;
        public string Package { get; set; } = string.Empty;
        public string Type { get; set; } = "User App";
        
        public double AppSizeMb { get; set; }
        public double UserDataMb { get; set; }
        public double CacheMb { get; set; }
        
        public double TotalMb => AppSizeMb + UserDataMb + CacheMb;

        public string AppSizeText => AppSizeMb >= 1024 ? $"{AppSizeMb / 1024.0:F1} GB" : $"{AppSizeMb:F1} MB";
        public string UserDataText => UserDataMb >= 1024 ? $"{UserDataMb / 1024.0:F1} GB" : $"{UserDataMb:F1} MB";
        public string CacheText => CacheMb >= 1024 ? $"{CacheMb / 1024.0:F1} GB" : $"{CacheMb:F1} MB";
        public string TotalText => TotalMb >= 1024 ? $"{TotalMb / 1024.0:F1} GB" : $"{TotalMb:F1} MB";
    }
}
