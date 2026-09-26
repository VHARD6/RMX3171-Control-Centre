using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models
{
    public partial class AppProcessInfo : ObservableObject
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public double RamMb { get; set; }
        public string Importance { get; set; } = "Unknown";
        public bool IsProtected { get; set; }
        public PackageRiskLevel RiskLevel { get; set; } = PackageRiskLevel.UNKNOWN;
        public string RiskLevelString => RiskLevel.ToString();

        public bool CanForceStop { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isExcludedFromQuickClean;
    }
}
