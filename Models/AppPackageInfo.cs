using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models
{
    public partial class AppPackageInfo : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelectedForAction;

        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public bool IsSystem { get; set; }
        public bool IsProtected { get; set; }
        public bool IsEnabled { get; set; }
        public double RamMb { get; set; }
        public string Version { get; set; } = "Unknown";
        public PackageRiskLevel RiskLevel { get; set; } = PackageRiskLevel.UNKNOWN;
        public string RiskLevelString => RiskLevel.ToString();
        
        public bool IsUserApp => !IsSystem && RiskLevel != PackageRiskLevel.PROTECTED;
    }
}
