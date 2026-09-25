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
        public string Accumulated { get; set; } = "";
    }

    public partial class CapacityTesterViewModel : ViewModelBase
    {
        private readonly IBatteryService _batteryService;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isPaused;
        [ObservableProperty] private bool _isFinished;
        [ObservableProperty] private string _statusText = "Not running";
        [ObservableProperty] private string _errorText = "";

        // Settings
        [ObservableProperty] private int _startPercentage = 80;
        [ObservableProperty] private int _stopPercentage = 30;

        // Current telemetry
        [ObservableProperty] private string _currentDraw = "—";
        [ObservableProperty] private string _voltage = "—";
        [ObservableProperty] private string _temperature = "—";
        [ObservableProperty] private string _batteryLevel = "—";
        [ObservableProperty] private string _elapsedTime = "00:00:00";
        [ObservableProperty] private string _estimatedDischarged = "0.0 mAh";

        // Results
        [ObservableProperty] private string _empiricalResult = "—";
        [ObservableProperty] private string _ratedRatio = "—";
        [ObservableProperty] private string _resultSummary = "";
        [ObservableProperty] private double _totalDischargedMah;

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
                ErrorText = "Start MUST be greater than Stop (e.g., 80 → 30).";
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
                ErrorText = "Test cannot start while the phone is charging.\nDisconnect USB power or use Wireless ADB.";
                return;
            }

            LogEntries.Clear();
            ErrorText = "";
            IsRunning = true;
            IsPaused = false;
            IsFinished = false;
            TotalDischargedMah = 0;
            EstimatedDischarged = "0 mAh";
            StatusText = "Measuring discharge...";
            
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

        private async Task RunTelemetryLoopAsync(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            DateTime lastTime = DateTime.UtcNow;
            
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
                    if (info.Level.Value > StartPercentage)
                    {
                        StatusText = $"Waiting for battery to drain to {StartPercentage}%...";
                    }
                    else if (info.Level.Value <= StopPercentage)
                    {
                        StatusText = "Target percentage reached. Completing test...";
                        CompleteTest(sw.Elapsed);
                        break;
                    }
                    else if (!IsPaused)
                    {
                        StatusText = "Measuring discharge...";
                        
                        if (info.StatusRaw != 3) // Charging or Full
                        {
                            IsPaused = true;
                            StatusText = "PAUSED — PHONE IS CHARGING";
                        }
                        else if (info.OppoBatteryCurrent.HasValue)
                        {
                            double dtHours = (now - lastTime).TotalHours;
                            int currentMa = Math.Abs(info.OppoBatteryCurrent.Value);
                            
                            double addedMah = currentMa * dtHours;
                            TotalDischargedMah += addedMah;
                            EstimatedDischarged = $"{Math.Round(TotalDischargedMah, 0)} mAh";

                            // Log ~every 15 seconds
                            if (sw.ElapsedMilliseconds % 15000 < 3000)
                            {
                                AddLogEntry(info, currentMa, TotalDischargedMah);
                            }
                        }
                    }
                }

                lastTime = now;
                await Task.Delay(3000, token);
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
                return;
            }

            BatteryLevel = $"{info.Level}%";
            Temperature = info.TemperatureCelsius.HasValue ? $"{info.TemperatureCelsius:F1} °C" : "Unknown";
            Voltage = info.VoltageVolts.HasValue ? $"{info.VoltageVolts:F2} V" : "Unknown";
            CurrentDraw = info.OppoBatteryCurrent.HasValue ? $"{info.OppoBatteryCurrent.Value} mA" : "Unknown";
            ElapsedTime = elapsed.ToString(@"hh\:mm\:ss");
        }

        private void AddLogEntry(BatteryInfo info, int currentMa, double accumMah)
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
                    Accumulated = $"{Math.Round(accumMah, 0)} mAh"
                });
            });
        }

        private void CompleteTest(TimeSpan elapsed)
        {
            IsFinished = true;
            IsRunning = false;
            
            int percentagePoints = StartPercentage - StopPercentage;
            if (percentagePoints > 0)
            {
                double estimatedTotalCapacity = (TotalDischargedMah / percentagePoints) * 100.0;
                EmpiricalResult = $"~{Math.Round(estimatedTotalCapacity, 0):N0} mAh";
                
                double rated = 6000.0;
                double ratio = (estimatedTotalCapacity / rated) * 100.0;
                RatedRatio = $"{Math.Round(ratio, 0)}%";

                ResultSummary = $"CAPACITY TEST RESULT\n\nStart:\n{StartPercentage}%\n\nStop:\n{StopPercentage}%\n\nMeasured discharge:\n{Math.Round(TotalDischargedMah, 0)} mAh\n\nEstimated full capacity:\n{EmpiricalResult}\n\nRated capacity:\n6000 mAh\n\nApprox. rated-capacity ratio:\n{RatedRatio}\n\nElapsed:\n{elapsed.Hours}h {elapsed.Minutes}m\n\nResults can vary with workload, temperature, battery age, measurement accuracy, and test conditions.";
            }
        }
    }
}
