using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;

namespace RMX3171ControlCentre.ViewModels
{
    public class CapacityTestLogEntry
    {
        public string Time { get; set; } = "";
        public string Status { get; set; } = "";
        public string Battery { get; set; } = "";
        public string Current { get; set; } = "";
        public string Voltage { get; set; } = "";
        public string Temp { get; set; } = "";
        public string IntegratedMah { get; set; } = "";
        public string ChargeCounterDelta { get; set; } = "";
    }

    public partial class CapacityTesterViewModel : ViewModelBase
    {
        private readonly IBatteryService _batteryService;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private bool _isArmed;
        [ObservableProperty] private bool _isFinished;
        [ObservableProperty] private string _statusText = "Not running";
        [ObservableProperty] private string _errorText = "";

        // Settings
        [ObservableProperty] private int _startPercentage = 80;
        [ObservableProperty] private int _stopPercentage = 30;
        [ObservableProperty] private bool _useTransitionGating = false;

        // Live Primary & Secondary Telemetry
        [ObservableProperty] private string _currentDraw = "—";
        [ObservableProperty] private string _voltage = "—";
        [ObservableProperty] private string _temperature = "—";
        [ObservableProperty] private string _batteryLevel = "—";
        [ObservableProperty] private string _chargeCounter = "—";
        [ObservableProperty] private string _elapsedTime = "00:00:00";
        [ObservableProperty] private string _estimatedDischarged = "0 mAh";
        [ObservableProperty] private string _chargeCounterDeltaText = "0 mAh";
        [ObservableProperty] private string _telemetryDifferenceText = "0.0%";

        // Results
        [ObservableProperty] private string _empiricalResult = "—";
        [ObservableProperty] private string _ratedRatio = "—";
        [ObservableProperty] private string _resultSummary = "";
        [ObservableProperty] private double _totalDischargedMah;
        [ObservableProperty] private double _chargeCounterDeltaMah;

        // Snapshots
        public int? StartSnapLevel { get; private set; }
        public double? StartSnapChargeCounterMah { get; private set; }
        public int? StartSnapCurrentMa { get; private set; }
        public double? StartSnapVoltageV { get; private set; }
        public double? StartSnapTempC { get; private set; }
        public DateTime? StartSnapTime { get; private set; }

        public int? StopSnapLevel { get; private set; }
        public double? StopSnapChargeCounterMah { get; private set; }
        public int? StopSnapCurrentMa { get; private set; }
        public double? StopSnapVoltageV { get; private set; }
        public double? StopSnapTempC { get; private set; }
        public DateTime? StopSnapTime { get; private set; }

        public ObservableCollection<CapacityTestLogEntry> LogEntries { get; } = new();

        public CapacityTesterViewModel(IBatteryService batteryService)
        {
            _batteryService = batteryService;
        }

        [RelayCommand]
        private async Task StartTestAsync()
        {
            if (IsRunning) return;

            if (StartPercentage <= StopPercentage)
            {
                ErrorText = "Start percentage MUST be greater than stop percentage (e.g., 80 → 30).";
                return;
            }

            var info = await _batteryService.GetBatteryInfoAsync();
            if (info == null || !info.Level.HasValue)
            {
                ErrorText = "Cannot read battery data. Ensure device is connected.";
                return;
            }

            if (info.StatusRaw != 3) // 3 = Discharging
            {
                ErrorText = "Test cannot start while phone is charging.\nDisconnect USB power or use Wireless ADB.";
                return;
            }

            if (UseTransitionGating && info.Level.Value <= StartPercentage)
            {
                ErrorText = $"Battery is already at {info.Level.Value}%. To wait for a transition down into {StartPercentage}%,\ncharge the device above {StartPercentage}%, set Start to {info.Level.Value - 1}%, or disable transition gating.";
                return;
            }

            LogEntries.Clear();
            ErrorText = "";
            IsRunning = true;
            IsPaused = false;
            IsFinished = false;
            TotalDischargedMah = 0;
            ChargeCounterDeltaMah = 0;
            EstimatedDischarged = "0 mAh";
            ChargeCounterDeltaText = "0 mAh";
            TelemetryDifferenceText = "0.0%";
            ResultSummary = "";

            ClearSnapshots();

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await RunTelemetryLoopAsync(_cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                if (!IsFinished) StatusText = "Stopped";
            }
            catch (Exception ex)
            {
                ErrorText = $"Error: {ex.Message}";
                StatusText = "Error";
            }
            finally
            {
                IsRunning = false;
                IsPaused = false;
                IsArmed = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private void StopTest()
        {
            _cancellationTokenSource?.Cancel();
        }

        [RelayCommand]
        private void ResumeTest()
        {
            if (IsPaused)
            {
                IsPaused = false;
                StatusText = "Measuring discharge...";
            }
        }

        private void ClearSnapshots()
        {
            StartSnapLevel = null;
            StartSnapChargeCounterMah = null;
            StartSnapCurrentMa = null;
            StartSnapVoltageV = null;
            StartSnapTempC = null;
            StartSnapTime = null;

            StopSnapLevel = null;
            StopSnapChargeCounterMah = null;
            StopSnapCurrentMa = null;
            StopSnapVoltageV = null;
            StopSnapTempC = null;
            StopSnapTime = null;
        }

        private void RecordStartSnapshot(BatteryInfo info)
        {
            StartSnapLevel = info.Level;
            StartSnapChargeCounterMah = info.ChargeCounterMah;
            StartSnapCurrentMa = info.OppoBatteryCurrent;
            StartSnapVoltageV = info.VoltageVolts;
            StartSnapTempC = info.TemperatureCelsius;
            StartSnapTime = DateTime.Now;
        }

        private void RecordStopSnapshot(BatteryInfo info)
        {
            StopSnapLevel = info.Level;
            StopSnapChargeCounterMah = info.ChargeCounterMah;
            StopSnapCurrentMa = info.OppoBatteryCurrent;
            StopSnapVoltageV = info.VoltageVolts;
            StopSnapTempC = info.TemperatureCelsius;
            StopSnapTime = DateTime.Now;
        }

        private async Task RunTelemetryLoopAsync(CancellationToken token)
        {
            var sw = new Stopwatch();
            DateTime lastTime = DateTime.UtcNow;
            bool integrationActive = false;

            if (UseTransitionGating)
            {
                IsArmed = true;
                StatusText = $"ARMED — Waiting for battery to transition to {StartPercentage}%...";
            }
            else
            {
                IsArmed = false;
            }

            while (!token.IsCancellationRequested)
            {
                var info = await _batteryService.GetBatteryInfoAsync();
                var now = DateTime.UtcNow;

                UpdateTelemetryUi(info, sw.Elapsed);

                if (info == null || !info.Level.HasValue)
                {
                    IsPaused = true;
                    StatusText = "PAUSED — CONNECTION LOST";
                }
                else
                {
                    // Check transition gating / arming
                    if (!integrationActive)
                    {
                        if (UseTransitionGating)
                        {
                            if (info.Level.Value > StartPercentage)
                            {
                                StatusText = $"ARMED — Waiting for battery to transition to {StartPercentage}% (current: {info.Level}%)...";
                            }
                            else if (info.Level.Value <= StartPercentage)
                            {
                                // Transition reached!
                                integrationActive = true;
                                IsArmed = false;
                                sw.Start();
                                lastTime = now;
                                RecordStartSnapshot(info);
                                StatusText = "Measuring discharge...";
                            }
                        }
                        else
                        {
                            // Immediate start
                            integrationActive = true;
                            sw.Start();
                            lastTime = now;
                            RecordStartSnapshot(info);
                            StatusText = "Measuring discharge...";
                        }
                    }

                    // Active integration
                    if (integrationActive)
                    {
                        if (info.Level.Value <= StopPercentage)
                        {
                            RecordStopSnapshot(info);
                            StatusText = "Target percentage reached. Completing test...";
                            CompleteTest(sw.Elapsed, info);
                            break;
                        }

                        if (!IsPaused)
                        {
                            if (info.StatusRaw != 3) // Charging or full
                            {
                                IsPaused = true;
                                StatusText = "PAUSED — PHONE IS CHARGING";
                            }
                            else if (info.OppoBatteryCurrent.HasValue)
                            {
                                double dtHours = (now - lastTime).TotalHours;
                                int currentMa = Math.Abs(info.OppoBatteryCurrent.Value);

                                // Numerical integration (Primary Telemetry)
                                double addedMah = currentMa * dtHours;
                                TotalDischargedMah += addedMah;
                                EstimatedDischarged = $"{Math.Round(TotalDischargedMah, 0)} mAh";

                                // Charge counter delta (Secondary Telemetry)
                                if (StartSnapChargeCounterMah.HasValue && info.ChargeCounterMah.HasValue)
                                {
                                    ChargeCounterDeltaMah = Math.Max(0, StartSnapChargeCounterMah.Value - info.ChargeCounterMah.Value);
                                    ChargeCounterDeltaText = $"{Math.Round(ChargeCounterDeltaMah, 0)} mAh";

                                    if (ChargeCounterDeltaMah > 0)
                                    {
                                        double diff = Math.Abs(TotalDischargedMah - ChargeCounterDeltaMah) / ChargeCounterDeltaMah * 100.0;
                                        TelemetryDifferenceText = $"{diff:F1}%";
                                    }
                                }

                                // Log snapshot approximately every 15 seconds
                                if (sw.ElapsedMilliseconds % 15000 < 3000)
                                {
                                    AddLogEntry(info, currentMa, TotalDischargedMah, ChargeCounterDeltaMah);
                                }
                            }
                        }
                    }
                }

                lastTime = now;
                await Task.Delay(1600, token);
            }
        }

        private void UpdateTelemetryUi(BatteryInfo info, TimeSpan elapsed)
        {
            if (info == null || !info.Level.HasValue)
            {
                BatteryLevel = "Unknown";
                Temperature = "Unknown";
                Voltage = "Unknown";
                CurrentDraw = "Unknown";
                ChargeCounter = "Unknown";
                return;
            }

            BatteryLevel = $"{info.Level}%";
            Temperature = info.TemperatureCelsius.HasValue ? $"{info.TemperatureCelsius:F1} °C" : "Unknown";
            Voltage = info.VoltageVolts.HasValue ? $"{info.VoltageVolts:F2} V" : "Unknown";
            CurrentDraw = info.OppoBatteryCurrent.HasValue ? $"{info.OppoBatteryCurrent.Value} mA" : "Unknown";
            ChargeCounter = info.ChargeCounterMah.HasValue ? $"{info.ChargeCounterMah.Value:N0} mAh" : "Unknown";
            ElapsedTime = elapsed.ToString(@"hh\:mm\:ss");
        }

        private void AddLogEntry(BatteryInfo info, int currentMa, double accumMah, double ccDeltaMah)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Insert(0, new CapacityTestLogEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Status = info.StatusLabel,
                    Battery = $"{info.Level}%",
                    Current = $"{currentMa} mA",
                    Voltage = info.VoltageVolts.HasValue ? $"{info.VoltageVolts:F2} V" : "-",
                    Temp = info.TemperatureCelsius.HasValue ? $"{info.TemperatureCelsius:F1}°C" : "-",
                    IntegratedMah = $"{Math.Round(accumMah, 0)} mAh",
                    ChargeCounterDelta = $"{Math.Round(ccDeltaMah, 0)} mAh"
                });
            });
        }

        private void CompleteTest(TimeSpan elapsed, BatteryInfo info)
        {
            IsFinished = true;
            IsRunning = false;
            IsArmed = false;

            int percentagePoints = (StartSnapLevel ?? StartPercentage) - (StopSnapLevel ?? StopPercentage);
            if (percentagePoints > 0)
            {
                double estimatedTotalCapacity = (TotalDischargedMah / percentagePoints) * 100.0;
                EmpiricalResult = $"~{Math.Round(estimatedTotalCapacity, 0):N0} mAh";

                double rated = 6000.0;
                double ratio = (estimatedTotalCapacity / rated) * 100.0;
                RatedRatio = $"{Math.Round(ratio, 0)}%";

                double diffPercent = 0;
                if (ChargeCounterDeltaMah > 0)
                {
                    diffPercent = Math.Abs(TotalDischargedMah - ChargeCounterDeltaMah) / ChargeCounterDeltaMah * 100.0;
                }

                double quantizationUncertainty = (1.0 / percentagePoints) * 100.0;

                ResultSummary = $"CAPACITY TEST RESULT\n\n" +
                                $"Measurement Method:\n" +
                                $"Current integration (Primary) + charge-counter cross-check (Secondary)\n\n" +
                                $"Start Snapshot:\n" +
                                $"• Displayed SOC: {StartSnapLevel}%\n" +
                                $"• Charge counter: {StartSnapChargeCounterMah:N0} mAh\n" +
                                $"• Voltage: {StartSnapVoltageV:F2} V\n" +
                                $"• Temperature: {StartSnapTempC:F1} °C\n" +
                                $"• Time: {StartSnapTime:HH:mm:ss}\n\n" +
                                $"Stop Snapshot:\n" +
                                $"• Displayed SOC: {StopSnapLevel}%\n" +
                                $"• Charge counter: {StopSnapChargeCounterMah:N0} mAh\n" +
                                $"• Voltage: {StopSnapVoltageV:F2} V\n" +
                                $"• Temperature: {StopSnapTempC:F1} °C\n" +
                                $"• Time: {StopSnapTime:HH:mm:ss}\n\n" +
                                $"Discharge Telemetry:\n" +
                                $"• Integrated discharged capacity: {Math.Round(TotalDischargedMah, 0)} mAh\n" +
                                $"• Charge-counter cross-check delta: {Math.Round(ChargeCounterDeltaMah, 0)} mAh\n" +
                                $"• Cross-check difference: {diffPercent:F1}%\n\n" +
                                $"Capacity Estimation:\n" +
                                $"• Empirical estimated full capacity: {EmpiricalResult}\n" +
                                $"• Rated capacity: 6,000 mAh\n" +
                                $"• Approximate ratio to rated 6000 mAh: {RatedRatio}\n" +
                                $"• Measurement uncertainty (quantization boundary): ±{quantizationUncertainty:F1}%\n" +
                                $"• Elapsed time: {elapsed.Hours}h {elapsed.Minutes}m\n\n" +
                                $"Note: Current is sampled at 1 mA reported resolution. Results can vary with workload, temperature, discharge rate, and test conditions. The charge-counter cross-check validates the integration against the phone's nominal SOC model, not independent battery degradation. This is an empirical capacity estimate under test conditions, NOT an official battery health or SOH reading.";
            }
        }
    }
}
