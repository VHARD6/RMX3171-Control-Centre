namespace RMX3171ControlCentre.Models
{
    public class AppProcessInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public double RamMb { get; set; }
        public string Importance { get; set; } = "Unknown";
        public bool IsProtected { get; set; }
    }
}
