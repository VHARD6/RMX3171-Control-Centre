namespace RMX3171ControlCentre.Models
{
    public class AppPackageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public bool IsProtected { get; set; }
        public bool IsEnabled { get; set; }
        public double RamMb { get; set; }
        public string Version { get; set; } = "Unknown";
    }
}
