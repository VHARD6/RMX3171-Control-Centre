using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.ViewModels
{
    public class BatteryPropertyRow
    {
        public string Property { get; set; } = "";
        public string Value { get; set; } = "";
        public string Unit { get; set; } = "";
        public string Meaning { get; set; } = "";
    }

    public partial class BatteryViewModel : ViewModelBase
    {
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;

        [ObservableProperty] private int _batteryLevel = 0;
        [ObservableProperty] private string _batteryLevelText = "–";
        [ObservableProperty] private string _chargingSource = "–";
        [ObservableProperty] private string _temperature = "Loading...";
        [ObservableProperty] private string _voltage = "Loading...";
        [ObservableProperty] private string _technology = "Loading...";
        [ObservableProperty] private string _healthStatus = "–";
        [ObservableProperty] private bool _healthIsGood = false;
        [ObservableProperty] private string _capacityHealth = "Not available";
        [ObservableProperty] private string _cycleCount = "Not exposed by device";
        [ObservableProperty] private string _healthSummaryText = "";
        [ObservableProperty] private string _fastCharging = "No";
        [ObservableProperty] private string _chargeCounter = "Loading...";
        [ObservableProperty] private string _chargeCounterNote = "";
        [ObservableProperty] private bool _isAdvancedVisible = false;
        [ObservableProperty] private string _rawDumpsysBattery = "Available on manual refresh";
        [ObservableProperty] private string _rawSysPowerSupply = "";
        [ObservableProperty] private bool _isRefreshing;
        [ObservableProperty] private string _lastUpdatedText = "Waiting for data...";
        [ObservableProperty] private bool _isStale = false;

        public ObservableCollection<BatteryPropertyRow> AdvancedRows { get; } = new();

        public BatteryViewModel(RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService)
        {
            _telemetryService = telemetryService;

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var snap = _telemetryService.CurrentSnapshot.Battery;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (snap.LastUpdated != System.DateTime.MinValue)
                    {
                        BatteryLevel = snap.Percentage;
                        BatteryLevelText = $"{snap.Percentage}%";
                        ChargingSource = snap.Status;
                        Temperature = $"{snap.Temperature:F1} °C";
                        Voltage = $"{snap.Voltage:F3} V";
                        Technology = string.IsNullOrEmpty(snap.Technology) ? "Unknown" : snap.Technology;
                        HealthStatus = snap.Health;
                        HealthIsGood = snap.Health == "Good";
                        HealthSummaryText = HealthIsGood ? "Battery health is reported as good." : "Battery health issue reported.";
                        ChargeCounter = snap.ChargeCounter > 0 ? $"{snap.ChargeCounter} mAh" : "Not exposed by device";
                        
                        var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                        IsStale = secondsAgo > 5;
                        LastUpdatedText = IsStale ? $"Last updated {secondsAgo:F1}s ago" : "Live";
                    }
                }));
            };
        }

        [RelayCommand]
        private void ToggleAdvanced() => IsAdvancedVisible = !IsAdvancedVisible;

        [RelayCommand]
        private async Task RefreshBatteryAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                await _telemetryService.ForceRefreshAsync("All");
            }
            finally
            {
                IsRefreshing = false;
            }
        }
    }
}
