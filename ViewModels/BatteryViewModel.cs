using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;
using System;
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
        private readonly IBatteryService _batteryService;

        // ── Main display ──────────────────────────────────────────────────────
        [ObservableProperty] private int _batteryLevel = 0;
        [ObservableProperty] private string _batteryLevelText = "–";
        [ObservableProperty] private string _chargingSource = "–";
        [ObservableProperty] private string _temperature = "Not available through ADB";
        [ObservableProperty] private string _voltage = "Not available through ADB";
        [ObservableProperty] private string _technology = "Not available through ADB";

        // ── Health card ───────────────────────────────────────────────────────
        [ObservableProperty] private string _healthStatus = "–";
        [ObservableProperty] private bool _healthIsGood = false;
        [ObservableProperty] private string _capacityHealth = "Not available";
        [ObservableProperty] private string _cycleCount = "Not exposed by device";
        [ObservableProperty] private string _healthSummaryText = "";

        // ── Charging section ──────────────────────────────────────────────────
        [ObservableProperty] private string _fastCharging = "No";
        [ObservableProperty] private string _chargeCounter = "Not available through ADB";
        [ObservableProperty] private string _chargeCounterNote = "";

        // ── Advanced toggle ───────────────────────────────────────────────────
        [ObservableProperty] private bool _isAdvancedVisible = false;
        [ObservableProperty] private string _rawDumpsysBattery = "Refresh to load raw data.";
        [ObservableProperty] private string _rawSysPowerSupply = "";

        public ObservableCollection<BatteryPropertyRow> AdvancedRows { get; } = new();

        public BatteryViewModel(IBatteryService batteryService)
        {
            _batteryService = batteryService;
        }

        [RelayCommand]
        private void ToggleAdvanced() => IsAdvancedVisible = !IsAdvancedVisible;

        [RelayCommand]
        private async Task RefreshBatteryAsync()
        {
            BatteryLevelText = "Loading…";
            ChargingSource = "–";

            BatteryInfo info;
            try
            {
                info = await _batteryService.GetBatteryInfoAsync();
            }
            catch (Exception ex)
            {
                BatteryLevelText = "Error";
                ChargingSource = ex.Message;
                return;
            }

            PopulateFromInfo(info);
        }

        private void PopulateFromInfo(BatteryInfo info)
        {
            // ── Level ─────────────────────────────────────────────────────────
            if (info.Level.HasValue)
            {
                BatteryLevel = info.Level.Value;
                BatteryLevelText = $"{info.Level.Value}%";
            }
            else
            {
                BatteryLevel = 0;
                BatteryLevelText = "Not available through ADB";
            }

            // ── Charging source ───────────────────────────────────────────────
            ChargingSource = info.ChargingSourceLabel;

            // ── Temperature ───────────────────────────────────────────────────
            Temperature = info.TemperatureCelsius.HasValue
                ? $"{info.TemperatureCelsius.Value:F1} °C"
                : "Not available through ADB";

            // ── Voltage ───────────────────────────────────────────────────────
            Voltage = info.VoltageVolts.HasValue
                ? $"{info.VoltageVolts.Value:F3} V"
                : "Not available through ADB";

            // ── Technology ────────────────────────────────────────────────────
            Technology = string.IsNullOrWhiteSpace(info.Technology)
                ? "Not available through ADB"
                : info.Technology;

            // ── Health ────────────────────────────────────────────────────────
            HealthStatus = info.HealthLabel;
            HealthIsGood = info.HealthIsGood;

            // ── Capacity health ───────────────────────────────────────────────
            if (info.CapacityHealthPercent.HasValue)
                CapacityHealth = $"{info.CapacityHealthPercent.Value:F1}%";
            else
                CapacityHealth = "Not available";

            // ── Cycle count ───────────────────────────────────────────────────
            CycleCount = info.CycleCount.HasValue
                ? info.CycleCount.Value.ToString()
                : "Not exposed by device";

            // ── Fast charging ─────────────────────────────────────────────────
            FastCharging = info.OppoFastCharger.HasValue
                ? (info.OppoFastCharger.Value ? "Yes (OPPO fast charge)" : "No")
                : "Not available through ADB";

            // ── Charge counter ────────────────────────────────────────────────
            if (info.ChargeCounterMah.HasValue)
            {
                ChargeCounter = $"{info.ChargeCounterMah.Value:F0} mAh (converted from µAh)";
                ChargeCounterNote =
                    "Android's charge counter is in µAh. " +
                    "This represents the current remaining charge, not the battery design capacity.";
            }
            else
            {
                ChargeCounter = "Not available through ADB";
                ChargeCounterNote = "";
            }

            // ── Health summary text ───────────────────────────────────────────
            if (info.CapacityHealthPercent.HasValue)
            {
                HealthSummaryText =
                    $"Android reports the battery as {info.HealthLabel.ToLower()}. " +
                    $"Estimated capacity health: {info.CapacityHealthPercent.Value:F1}% " +
                    $"(based on /sys/class/power_supply/battery/charge_full vs charge_full_design).";
            }
            else
            {
                HealthSummaryText =
                    "Android reports the battery as healthy, but this device does not currently " +
                    "expose enough information through /sys to calculate how much of its original " +
                    "capacity remains. This is normal on many Android 11 devices.";
            }

            // ── Raw data ──────────────────────────────────────────────────────
            RawDumpsysBattery = info.RawDumpsysBattery;
            RawSysPowerSupply = info.RawSysPowerSupply;

            // ── Advanced table ────────────────────────────────────────────────
            BuildAdvancedTable(info);
        }

        private void BuildAdvancedTable(BatteryInfo info)
        {
            AdvancedRows.Clear();
            void Add(string prop, string val, string unit, string meaning) =>
                AdvancedRows.Add(new BatteryPropertyRow { Property = prop, Value = val, Unit = unit, Meaning = meaning });

            string N(object? v) => v?.ToString() ?? "—";

            Add("Level",                   N(info.Level),                           "%",    "Battery percentage reported by Android.");
            Add("Status",                  $"{info.StatusLabel} ({N(info.StatusRaw)})", "", "Android BatteryManager status constant.");
            Add("Health",                  $"{info.HealthLabel} ({N(info.HealthRaw)})", "", "Android OS-reported battery health. Not the same as remaining capacity.");
            Add("Technology",              N(info.Technology),                       "",    "Battery chemistry type.");
            Add("Voltage",                 N(info.VoltageMillivolts),               "mV",  "Battery voltage as reported by Android.");
            Add("Temperature",             N(info.TemperatureTenths),               "×0.1 °C", "Android temp × 0.1 = °C. e.g. 370 → 37.0 °C.");
            Add("Charge counter",          N(info.ChargeCounterMicroAh),            "µAh", "BATTERY_PROPERTY_CHARGE_COUNTER. Current remaining charge. Divide by 1000 for mAh.");
            Add("Max charging current",    N(info.MaxChargingCurrentMicroAmps),     "µA",  "Max current the charger is allowed to supply. Divide by 1000 for mA.");
            Add("Max charging voltage",    N(info.MaxChargingVoltageMicroVolts),    "µV",  "Max voltage the charger is allowed to supply. Divide by 1000 for mV.");
            Add("AC powered",              N(info.AcPowered),                        "",   "True if AC adapter is connected.");
            Add("USB powered",             N(info.UsbPowered),                       "",   "True if USB cable is connected.");
            Add("Wireless powered",        N(info.WirelessPowered),                  "",   "True if wireless charger is connected.");
            Add("cycle_count (/sys)",      N(info.CycleCount),                       "",   "Charge cycle count from /sys/class/power_supply/battery/cycle_count.");
            Add("charge_full (/sys)",      N(info.FullChargeCapacityMicroAh),       "µAh", "Full charge capacity from /sys. Divide by 1000 for mAh.");
            Add("charge_full_design (/sys)", N(info.DesignCapacityMicroAh),         "µAh", "Design capacity from /sys. Divide by 1000 for mAh.");

            Add("OPPO Charger voltage",    N(info.OppoChargerVoltage),              "(raw)", "OPPO vendor charger voltage. Exact unit is driver-dependent.");
            Add("OPPO Battery current",    N(info.OppoBatteryCurrent),              "(raw)", "OPPO vendor battery current. Negative = discharging.");
            Add("OPPO ChargerTechnology",  N(info.OppoChargerTechnology),            "",     "0 = standard. Higher values may indicate fast charge protocols.");
            Add("OPPO FastCharger",        N(info.OppoFastCharger),                  "",     "OPPO fast-charge flag.");
            Add("OPPO BatteryHwStatus",    N(info.OppoBatteryHwStatus),              "",     "Hardware battery status from OPPO driver.");
            Add("OPPO BatteryIcStatus",    N(info.OppoBatteryIcStatus),              "",     "Battery IC (integrated circuit) status from OPPO driver.");
            Add("OPPO PhoneTemp",          N(info.OppoPhoneTempRaw),                 "(raw)", "-1023 means sensor not available on this device.");
        }
    }
}
