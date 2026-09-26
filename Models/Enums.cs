namespace RMX3171ControlCentre.Models
{
    public enum AppMode
    {
        ReadOnly,
        Advanced
    }

    public enum RiskLevel
    {
        Safe,
        Modify,
        HighRisk
    }

    public enum PackageRiskLevel
    {
        SAFE_USER,
        USER,
        SYSTEM,
        PROTECTED,
        UNKNOWN
    }
}
