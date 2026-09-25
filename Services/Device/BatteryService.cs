using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Adb;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.Services.Device
{
    /// <summary>
    /// Parses READ-ONLY ADB battery data specifically for the RMX3171 (Realme Narzo 30A).
    /// Handles both the standard Android BatteryManager output and the OPPO vendor
    /// battery service block present in dumpsys battery on this device.
    /// </summary>
    public interface IBatteryService
    {
        Task<BatteryInfo> GetBatteryInfoAsync();
    }

    public class BatteryService : IBatteryService
    {
        private readonly IAdbService _adbService;

        public BatteryService(IAdbService adbService)
        {
            _adbService = adbService;
        }

        public async Task<BatteryInfo> GetBatteryInfoAsync()
        {
            var info = new BatteryInfo();

            // ── 1. dumpsys battery  (primary source) ─────────────────────────
            var dumpsys = await _adbService.ExecuteCommandAsync("shell dumpsys battery");
            if (dumpsys.ExitCode == 0)
            {
                info.RawDumpsysBattery = dumpsys.Output;
                ParseDumpsysBattery(dumpsys.Output, info);
            }
            else
            {
                info.RawDumpsysBattery = $"Command failed (exit {dumpsys.ExitCode}): {dumpsys.Error}";
            }

            // ── 2. /sys/class/power_supply/battery  (capacity / cycle count) ─
            // READ-ONLY cat commands – safe on Android 11 without root
            var sysPaths = new[]
            {
                "shell cat /sys/class/power_supply/battery/cycle_count",
                "shell cat /sys/class/power_supply/battery/charge_full",        // µAh – full charge capacity
                "shell cat /sys/class/power_supply/battery/charge_full_design", // µAh – design capacity
            };

            var sysResults = new Dictionary<string, string>();
            foreach (var cmd in sysPaths)
            {
                var r = await _adbService.ExecuteCommandAsync(cmd);
                var key = cmd.Split('/')[^1]; // last segment
                sysResults[key] = r.ExitCode == 0 ? r.Output.Trim() : "";
            }

            if (int.TryParse(sysResults.GetValueOrDefault("cycle_count", ""), out int cc))
                info.CycleCount = cc;

            if (int.TryParse(sysResults.GetValueOrDefault("charge_full", ""), out int chFull) && chFull > 0)
                info.FullChargeCapacityMicroAh = chFull;

            if (int.TryParse(sysResults.GetValueOrDefault("charge_full_design", ""), out int chDesign) && chDesign > 0)
                info.DesignCapacityMicroAh = chDesign;

            // Build summary of /sys readings for Advanced panel
            info.RawSysPowerSupply = string.Join("\n", new[]
            {
                $"cycle_count         : {sysResults.GetValueOrDefault("cycle_count", "(not exposed)")}",
                $"charge_full         : {sysResults.GetValueOrDefault("charge_full", "(not exposed)")} µAh",
                $"charge_full_design  : {sysResults.GetValueOrDefault("charge_full_design", "(not exposed)")} µAh",
            });

            return info;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Parser for "adb shell dumpsys battery"
        //  Handles both the OPPO vendor block and the standard Android block.
        // ─────────────────────────────────────────────────────────────────────
        private static void ParseDumpsysBattery(string raw, BatteryInfo info)
        {
            bool inOppoSection = false;
            bool inStandardSection = false;

            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.Trim();

                // Section detection
                if (line.Contains("OPPO Battery Service state", StringComparison.OrdinalIgnoreCase))
                {
                    inOppoSection = true;
                    inStandardSection = false;
                    continue;
                }
                if (line.Contains("Battery Service state", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains("OPPO", StringComparison.OrdinalIgnoreCase))
                {
                    inStandardSection = true;
                    inOppoSection = false;
                    continue;
                }

                // ── OPPO block ────────────────────────────────────────────────
                if (inOppoSection)
                {
                    if (TryGetInt(line, "Charger voltage", out int cv))        info.OppoChargerVoltage = cv;
                    if (TryGetInt(line, "Battery current", out int bc))        info.OppoBatteryCurrent = bc;
                    if (TryGetInt(line, "ChargerTechnology", out int ct))      info.OppoChargerTechnology = ct;
                    if (TryGetBool(line, "ChargerFastCharger", out bool fc))   info.OppoFastCharger = fc;
                    if (TryGetInt(line, "BatteryHwStatus", out int hw))        info.OppoBatteryHwStatus = hw;
                    if (TryGetInt(line, "BatteryIcStatus", out int ic))        info.OppoBatteryIcStatus = ic;
                    if (TryGetInt(line, "PhoneTemp", out int pt))              info.OppoPhoneTempRaw = pt;
                }

                // ── Standard Android block ────────────────────────────────────
                if (inStandardSection)
                {
                    if (TryGetBool(line, "AC powered", out bool ac))           info.AcPowered = ac;
                    if (TryGetBool(line, "USB powered", out bool usb))         info.UsbPowered = usb;
                    if (TryGetBool(line, "Wireless powered", out bool wl))     info.WirelessPowered = wl;
                    if (TryGetInt(line, "Max charging current", out int mcc))  info.MaxChargingCurrentMicroAmps = mcc;
                    if (TryGetInt(line, "Max charging voltage", out int mcv))  info.MaxChargingVoltageMicroVolts = mcv;
                    if (TryGetLong(line, "Charge counter", out long counter))  info.ChargeCounterMicroAh = counter;
                    if (TryGetInt(line, "status", out int st))                 info.StatusRaw = st;
                    if (TryGetInt(line, "health", out int h))                  info.HealthRaw = h;
                    if (TryGetBool(line, "present", out bool pr))              info.Present = pr;
                    if (TryGetInt(line, "level", out int lvl))                 info.Level = lvl;
                    if (TryGetInt(line, "scale", out int sc))                  info.Scale = sc;
                    if (TryGetInt(line, "voltage", out int v))                 info.VoltageMillivolts = v;
                    if (TryGetInt(line, "temperature", out int t))             info.TemperatureTenths = t;

                    // Technology is a string value
                    var techMatch = Regex.Match(line, @"^technology\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (techMatch.Success) info.Technology = techMatch.Groups[1].Value.Trim();
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Parsing helpers – key : value
        // ─────────────────────────────────────────────────────────────────────
        private static bool TryGetInt(string line, string key, out int value)
        {
            value = 0;
            var pattern = $@"^{Regex.Escape(key)}\s*:\s*(-?\d+)";
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out value);
        }

        private static bool TryGetLong(string line, string key, out long value)
        {
            value = 0;
            var pattern = $@"^{Regex.Escape(key)}\s*:\s*(-?\d+)";
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            return m.Success && long.TryParse(m.Groups[1].Value, out value);
        }

        private static bool TryGetBool(string line, string key, out bool value)
        {
            value = false;
            var pattern = $@"^{Regex.Escape(key)}\s*:\s*(true|false)";
            var m = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            value = m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return true;
        }
    }
}
