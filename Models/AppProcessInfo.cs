using CommunityToolkit.Mvvm.ComponentModel;

namespace RMX3171ControlCentre.Models
{
    public partial class CacheAppInfo : ObservableObject
    {
        public string AppName { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        
        [ObservableProperty]
        private double _cacheSizeMb;
        
        public string FormattedSize => $"{CacheSizeMb:F2} MB";
        public string Category { get; set; } = string.Empty;
        public bool IsEligible { get; set; }
        public string ExcludeReason { get; set; } = string.Empty;
    }

    public partial class AppProcessInfo : ObservableObject
    {
        public string PackageName { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        
        private double _ramMb;
        public double RamMb 
        { 
            get => _ramMb; 
            set 
            {
                SetProperty(ref _ramMb, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        private bool _isDisabled;
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                SetProperty(ref _isDisabled, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        
        private string _disabledStateDetail = string.Empty;
        public string DisabledStateDetail
        {
            get => _disabledStateDetail;
            set
            {
                SetProperty(ref _disabledStateDetail, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText 
        {
            get 
            {
                if (IsDisabled) 
                {
                    return string.IsNullOrEmpty(DisabledStateDetail) ? "DISABLED" : DisabledStateDetail;
                }
                if (RamMb > 0) return "RUNNING";
                return "STOPPED";
            }
        }


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
