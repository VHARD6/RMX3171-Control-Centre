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
        public bool IsEnabled { get; set; }
        public double RamMb { get; set; }
        public string Version { get; set; } = "Unknown";
        
        public PackageRiskLevel RiskLevel { get; set; } = PackageRiskLevel.UNKNOWN;
        public string RiskLevelString => RiskLevel.ToString();

        public PackageRecommendation Recommendation { get; set; } = PackageRecommendation.UNKNOWN;
        public string RecommendationString => Recommendation.ToString().Replace("_", " ");
        
        public string Reason { get; set; } = string.Empty;
        public string Confidence { get; set; } = "Low";
        
        // Is it completely untouchable?
        public bool IsCritical => RiskLevel == PackageRiskLevel.CRITICAL;
        
        public bool IsUserApp => RiskLevel == PackageRiskLevel.LOW || RiskLevel == PackageRiskLevel.MODERATE && !IsSystem;
    }
}
