namespace RMX3171ControlCentre.Models
{
    public class MemoryInfo
    {
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double AvailableGB { get; set; }
        public double CachedGB { get; set; }
        public double FreeGB { get; set; }
        public double ZramTotalGB { get; set; }
        public double ZramUsedGB { get; set; }
        public string MemoryPressure { get; set; } = "Normal";
    }
}
