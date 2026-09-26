using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models
{
    public partial class AppProcessInfo : ObservableObject
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public double RamMb { get; set; }
        public string Importance { get; set; } = "Unknown";
        
        public PackageRiskLevel RiskLevel { get; set; } = PackageRiskLevel.UNKNOWN;
        public string RiskLevelString => RiskLevel.ToString();

        public PackageRecommendation Recommendation { get; set; } = PackageRecommendation.UNKNOWN;
        public string RecommendationString => Recommendation.ToString().Replace("_", " ");
        
        public string Reason { get; set; } = string.Empty;

        // Note: CanForceStop now depends on whether the user has enabled Expert Actions 
        // OR the RiskLevel is LOW. We'll manage this in the ViewModel.
        [ObservableProperty]
        private bool _canForceStop;

        // Is it fundamentally a system/native process that we don't want the user to touch?
        public bool IsCritical => RiskLevel == PackageRiskLevel.CRITICAL;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isExcludedFromQuickClean;
    }
}
