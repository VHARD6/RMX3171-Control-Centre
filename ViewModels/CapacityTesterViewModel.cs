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
        public int Level { get; set; }
        public int CurrentMa { get; set; }
        public double VoltageV { get; set; }
        public double TempC { get; set; }
        public double AccumulatedMah { get; set; }
    }

    public partial class CapacityTesterViewModel : ViewModelBase
    {
        private readonly IBatteryService _batteryService;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private bool _isFinished;
        [ObservableProperty] private string _statusText = "Not running";
        [ObservableProperty] private string _errorText = "";

        // Settings
        [ObservableProperty] private int _startPercentage = 80;
        [ObservableProperty] private int _stopPercentage = 30;

        // Current telemetry
        [ObservableProperty] private string _currentDraw = "–";
        [ObservableProperty] private string _voltage = "–";
        [ObservableProperty] private string _temperature = "–";
        [ObservableProperty] private string _batteryLevel = "–";
        [ObservableProperty] private string _elapsedTime = "00:00:00";
        [ObservableProperty] private string _estimatedDischarged = "0.0 mAh";

        // Results
        [ObservableProperty] private string _empiricalResult = "–";
        [ObservableProperty] private string _ratedRatio = "–";
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
                ErrorText = "Start percentage must be greater than stop percentage.";
                return;
            }

            LogEntries.Clear();
            ErrorText = "";
            IsRunning = true;
            IsFinished = false;
            TotalDischargedMah = 0;
            EstimatedDischarged = "0.0 mAh";
            StatusText = "Initializing...";
            
            _cancellationTokenSource = new CancellationTokenSource();
            
            try
            {
                await RunTelemetryLoopAsync(_cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                StatusText = "Stopped";
            }
            catch (Exception ex)
            {
                ErrorText = $"Error: {ex.Message}";
                StatusText = "Error";
            }
            finally
            {
                IsRunning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        [RelayCommand]
        private void StopTest()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async Task RunTelemetryLoopAsync(CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            DateTime lastTime = DateTime.UtcNow;
            
            BatteryInfo? prevInfo = null;

            while (!token.IsCancellationRequested)
            {
                var info = await _batteryService.GetBatteryInfoAsync();
                var now = DateTime.UtcNow;
                
                // Update UI bindings
                UpdateTelemetryUi(info, sw.Elapsed);

                if (info.Level.HasValue)
                {
                    if (info.Level.Value > StartPercentage)
                    {
                        StatusText = $"Waiting for battery to drain to {StartPercentage}%...";
                    }
                    else if (info.Level.Value <= StopPercentage)
                    {
                        StatusText = "Target percentage reached. Completing test...";
                        CompleteTest(info);
                        break;
                    }
                    else
                    {
                        StatusText = "Measuring discharge...";
                        
                        // Ensure we are discharging
                        if (info.StatusRaw != 3) // 3 = BATTERY_STATUS_DISCHARGING
                        {
                            StatusText = "PAUSED - Device is charging or not discharging.";
                        }
                        else if (prevInfo != null && info.OppoBatteryCurrent.HasValue)
                        {
                            // Calculate dt in hours
                            double dtHours = (now - lastTime).TotalHours;
                            
                            // OPPO Battery Current: negative when charging, positive when discharging
                            // (or vice-versa depending on the device, we assume absolute value for discharge to be safe 
                            //  since we explicitly checked StatusRaw == 3)
                            int currentMa = Math.Abs(info.OppoBatteryCurrent.Value);
                            
                            double addedMah = currentMa * dtHours;
                            TotalDischargedMah += addedMah;
                            EstimatedDischarged = $"{TotalDischargedMah:F1} mAh";

                            // Log every ~30 seconds or so (to avoid filling memory, we do it every 10 loops)
                            if (sw.ElapsedMilliseconds % 15000 < 5000)
                            {
                                AddLogEntry(info, currentMa, TotalDischargedMah);
                            }
                        }
                    }
                }

                prevInfo = info;
                lastTime = now;

                await Task.Delay(3000, token);
            }
        }

        private void UpdateTelemetryUi(BatteryInfo info, TimeSpan elapsed)
        {
            BatteryLevel = info.Level.HasValue ? $"{info.Level}%" : "Unknown";
            Temperature = info.TemperatureCelsius.HasValue ? $"{info.TemperatureCelsius:F1} °C" : "Unknown";
            Voltage = info.VoltageVolts.HasValue ? $"{info.VoltageVolts:F3} V" : "Unknown";
            
            if (info.OppoBatteryCurrent.HasValue)
            {
                CurrentDraw = $"{info.OppoBatteryCurrent.Value} mA";
            }
            else
            {
                CurrentDraw = "Unknown";
            }

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
                    Level = info.Level ?? 0,
                    CurrentMa = currentMa,
                    VoltageV = info.VoltageVolts ?? 0,
                    TempC = info.TemperatureCelsius ?? 0,
                    AccumulatedMah = Math.Round(accumMah, 1)
                });
            });
        }

        private void CompleteTest(BatteryInfo info)
        {
            IsFinished = true;
            
            // Empirical calculation
            int percentagePoints = StartPercentage - StopPercentage;
            if (percentagePoints > 0)
            {
                double estimatedTotalCapacity = (TotalDischargedMah / percentagePoints) * 100.0;
                EmpiricalResult = $"~{Math.Round(estimatedTotalCapacity, 0):N0} mAh";
                
                double rated = 6000.0; // RMX3171 rated capacity
                double ratio = (estimatedTotalCapacity / rated) * 100.0;
                RatedRatio = $"{ratio:F1}%";
            }
            else
            {
                EmpiricalResult = "Invalid range";
                RatedRatio = "–";
            }
        }
    }
}
