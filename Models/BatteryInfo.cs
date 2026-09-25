using System;
using System.Collections.Generic;

namespace RMX3171ControlCentre.Models
{
    /// <summary>
    /// Parsed, strongly-typed battery data from the RMX3171.
    /// All values are sourced from READ-ONLY ADB commands only.
    /// No values are estimated or guessed.
    /// </summary>
    public class BatteryInfo
    {
        // ── Standard Android BatteryManager fields ────────────────────────────
        public int? Level { get; set; }                          // 0–100 %
        public int? Scale { get; set; }                          // Usually 100
        public bool? AcPowered { get; set; }
        public bool? UsbPowered { get; set; }
        public bool? WirelessPowered { get; set; }
        public int? StatusRaw { get; set; }                      // BatteryManager.BATTERY_STATUS_*
        public int? HealthRaw { get; set; }                      // BatteryManager.BATTERY_HEALTH_*
        public bool? Present { get; set; }
        public int? VoltageMillivolts { get; set; }              // mV  (dumpsys: "voltage: 4125")
        public int? TemperatureTenths { get; set; }              // tenths °C (dumpsys: "temperature: 370" → 37.0 °C)
        public string? Technology { get; set; }                  // e.g. "Li-ion"
        public long? ChargeCounterMicroAh { get; set; }          // µAh (dumpsys: "Charge counter: 3720000")
        public int? MaxChargingCurrentMicroAmps { get; set; }    // µA
        public int? MaxChargingVoltageMicroVolts { get; set; }   // µV

        // ── OPPO/Realme vendor service fields ─────────────────────────────────
        // Source: "Current OPPO Battery Service state" block in dumpsys battery
        public int? OppoChargerVoltage { get; set; }             // raw – unit unclear from driver, shown as-is
        public int? OppoBatteryCurrent { get; set; }             // raw – likely mA, negative = discharging
        public int? OppoChargerTechnology { get; set; }          // 0 = standard
        public bool? OppoFastCharger { get; set; }
        public int? OppoBatteryHwStatus { get; set; }
        public int? OppoBatteryIcStatus { get; set; }
        public int? OppoPhoneTempRaw { get; set; }               // -1023 = sensor unavailable

        // ── Cycle count ───────────────────────────────────────────────────────
        // May be exposed via /sys/class/power_supply/battery/cycle_count
        public int? CycleCount { get; set; }

        // ── Design / full-charge capacity ─────────────────────────────────────
        // Exposed on some devices via /sys/class/power_supply/battery/
        // charge_full_design and charge_full (in µAh on Linux power_supply class)
        public int? DesignCapacityMicroAh { get; set; }
        public int? FullChargeCapacityMicroAh { get; set; }

        // ── Raw output strings for the Advanced panel ─────────────────────────
        public string RawDumpsysBattery { get; set; } = "";
        public string RawSysPowerSupply { get; set; } = "";

        // ══ Convenience computed properties ══════════════════════════════════

        public double? TemperatureCelsius =>
            TemperatureTenths.HasValue ? TemperatureTenths.Value / 10.0 : null;

        public double? VoltageVolts =>
            VoltageMillivolts.HasValue ? VoltageMillivolts.Value / 1000.0 : null;

        /// <summary>
        /// Charge counter converted from µAh to mAh.
        /// Android's BatteryManager.BATTERY_PROPERTY_CHARGE_COUNTER is defined in µAh.
        /// On the RMX3171 this matches the device's reported value.
        /// </summary>
        public double? ChargeCounterMah =>
            ChargeCounterMicroAh.HasValue ? Math.Round(ChargeCounterMicroAh.Value / 1000.0, 0) : null;

        public double? DesignCapacityMah =>
            DesignCapacityMicroAh.HasValue ? Math.Round(DesignCapacityMicroAh.Value / 1000.0, 0) : null;

        public double? FullChargeCapacityMah =>
            FullChargeCapacityMicroAh.HasValue ? Math.Round(FullChargeCapacityMicroAh.Value / 1000.0, 0) : null;

        public string StatusLabel => StatusRaw switch
        {
            1 => "Unknown",
            2 => "Charging",
            3 => "Discharging",
            4 => "Not charging",
            5 => "Full",
            _ => StatusRaw.HasValue ? $"Unknown status code ({StatusRaw})" : "Not available through ADB"
        };

        public string ChargingSourceLabel
        {
            get
            {
                if (AcPowered == true)       return "Charging via AC adapter";
                if (UsbPowered == true)      return "Charging via USB";
                if (WirelessPowered == true) return "Charging wirelessly";
                if (StatusRaw == 5)          return "Full (not drawing charge)";
                if (StatusRaw == 3)          return "On battery (not charging)";
                return "Not charging";
            }
        }

        public string HealthLabel => HealthRaw switch
        {
            1 => "Unknown",
            2 => "Good",
            3 => "Overheat",
            4 => "Dead",
            5 => "Overvoltage",
            6 => "Unspecified failure",
            7 => "Cold",
            _ => HealthRaw.HasValue ? $"Unknown ({HealthRaw})" : "Not available through ADB"
        };

        public bool HealthIsGood => HealthRaw == 2;

        /// <summary>
        /// Capacity health % – only returned when BOTH design and full-charge capacity
        /// are reliably available from /sys and both are > 0.
        /// Returns null if data is absent – we do NOT guess.
        /// </summary>
        public double? CapacityHealthPercent
        {
            get
            {
                if (DesignCapacityMicroAh is > 0 && FullChargeCapacityMicroAh is > 0)
                    return Math.Round(FullChargeCapacityMicroAh.Value * 100.0 / DesignCapacityMicroAh.Value, 1);
                return null;
            }
        }

        /// <summary>
        /// True if the OPPO phone temperature sensor returned a valid reading.
        /// -1023 is the "not available" sentinel on this platform.
        /// </summary>
        public bool OppoPhoneTempAvailable => OppoPhoneTempRaw.HasValue && OppoPhoneTempRaw.Value != -1023;
    }
}
