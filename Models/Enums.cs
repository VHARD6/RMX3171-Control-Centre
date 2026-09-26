namespace RMX3171ControlCentre.Models
{
    public enum AppMode
    {
        ReadOnly,
        Advanced,
        Expert
    }

    public enum RiskLevel
    {
        Safe,
        Modify,
        HighRisk
    }

    public enum PackageRiskLevel
    {
        LOW,
        MODERATE,
        HIGH,
        CRITICAL,
        UNKNOWN
    }

    public enum PackageRecommendation
    {
        REMOVE_CANDIDATE,
        OPTIONAL_COMPONENT,
        KEEP_RECOMMENDED,
        DO_NOT_REMOVE,
        UNKNOWN
    }
}
