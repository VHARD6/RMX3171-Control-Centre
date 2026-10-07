using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Device;
using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class MemoryManagerViewModel : ViewModelBase
    {
        private readonly IMemoryService _memoryService;
        private readonly IDialogService _dialogService;
        private readonly IAppModeService _appModeService;
        private readonly IAuditService _auditService;
        private readonly RMX3171ControlCentre.Services.Telemetry.ITelemetryService _telemetryService;
        private readonly ICleanerConfigService _configService;

        [ObservableProperty]
        private MemoryInfo _currentMemoryInfo = new MemoryInfo();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _userAppProcesses = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _vendorOptionalProcesses = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _systemProcesses = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _disabledAppProcesses = new ObservableCollection<AppProcessInfo>();
        
        [ObservableProperty]
        private string _disabledCountLabel = "DISABLED APPS (0)";

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private string _lastUpdatedText = "Press 'Scan Memory' to load.";

        [ObservableProperty]
        private string _lastCleanupSummary = "No cleanups performed this session.";

        [ObservableProperty]
        private string _userAppsTotalLabel = "0 MB (PSS)";

        [ObservableProperty]
        private string _vendorTotalLabel = "0 MB (PSS)";

        [ObservableProperty]
        private string _systemTotalLabel = "0 MB (PSS)";
        [ObservableProperty]
        private string _estimatedReclaimableLabel = "0 MB";

        [RelayCommand]
        private void SelectAllEligible()
        {
            foreach (var app in UserAppProcesses)
            {
                if (app.CanForceStop)
                    app.IsSelected = true;
            }
        }

        private void UpdateEstimatedReclaimable()
        {
            var selected = UserAppProcesses.Concat(VendorOptionalProcesses).Concat(SystemProcesses)
                .Where(a => a.IsSelected && a.CanForceStop).ToList();
            
            double total = selected.Sum(a => a.RamMb);
            EstimatedReclaimableLabel = $"{total:F0} MB";
        }


        [ObservableProperty]
        private string _dataSources = "—";

        public MemoryManagerViewModel(
            IMemoryService memoryService,
            IDialogService dialogService,
            IAppModeService appModeService,
            IAuditService auditService,
            RMX3171ControlCentre.Services.Telemetry.ITelemetryService telemetryService,
            ICleanerConfigService configService)
        {
            _memoryService  = memoryService;
            _dialogService  = dialogService;
            _appModeService = appModeService;
            _auditService   = auditService;
            _telemetryService = telemetryService;
            _configService  = configService;

            _appModeService.ModeChanged += (s, e) => {
                UpdateCanForceStop();
                UpdateEstimatedReclaimable();
            };

            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    var snap = _telemetryService.CurrentSnapshot.Memory;
                    if (snap.LastUpdated != System.DateTime.MinValue)
                    {
                        CurrentMemoryInfo.TotalGB     = snap.TotalRamMb     / 1024.0;
                        CurrentMemoryInfo.UsedGB      = snap.UsedRamMb      / 1024.0;
                        CurrentMemoryInfo.AvailableGB = snap.AvailableRamMb / 1024.0;
                        CurrentMemoryInfo.CachedGB    = snap.CachedRamMb    / 1024.0;
                        CurrentMemoryInfo.ZramTotalGB = snap.ZramTotalMb    / 1024.0;
                        CurrentMemoryInfo.ZramUsedGB  = snap.ZramUsedMb     / 1024.0;
                        CurrentMemoryInfo.FreeGB      = (snap.AvailableRamMb - snap.CachedRamMb) / 1024.0;

                        var secondsAgo = (System.DateTime.Now - snap.LastUpdated).TotalSeconds;
                        LastUpdatedText = secondsAgo > 5 ? $"Last updated {secondsAgo:F0}s ago" : "Live";

                        OnPropertyChanged(nameof(CurrentMemoryInfo));
                    }
                }));
            };
        }

        private void UpdateCanForceStop()
        {
            foreach (var app in UserAppProcesses.Concat(VendorOptionalProcesses).Concat(SystemProcesses))
            {
                if (app.IsCritical)
                {
                    app.CanForceStop = false;
                }
                else if (app.RiskLevel == PackageRiskLevel.LOW)
                {
                    app.CanForceStop = _appModeService.CurrentMode != AppMode.ReadOnly;
                }
                else if (app.RiskLevel == PackageRiskLevel.MODERATE || app.RiskLevel == PackageRiskLevel.HIGH || app.RiskLevel == PackageRiskLevel.UNKNOWN)
                {
                    app.CanForceStop = _appModeService.CurrentMode == AppMode.Expert;
                }
                else
                {
                    app.CanForceStop = false;
                }
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                await _telemetryService.ForceRefreshAsync("All");

                var exclusions = await _configService.GetExcludedPackagesAsync();
                var query = await _memoryService.GetProcessesAsync();

                DataSources = query.Sources;

                // Merge UserAppProcesses
                var existingUsers = UserAppProcesses.ToDictionary(a => a.PackageName);
                foreach (var app in query.UserApps)
                {
                    if (existingUsers.TryGetValue(app.PackageName, out var existing))
                    {
                        existing.RamMb = app.RamMb;
                        existingUsers.Remove(app.PackageName);
                    }
                    else
                    {
                        app.IsExcludedFromQuickClean = exclusions.Contains(app.PackageName);
                        app.PropertyChanged += async (s, e) =>
                        {
                            if (e.PropertyName == nameof(AppProcessInfo.IsSelected))
                            {
                                UpdateEstimatedReclaimable();
                            }
                            if (e.PropertyName == nameof(AppProcessInfo.IsExcludedFromQuickClean))
                            {
                                var current = await _configService.GetExcludedPackagesAsync();
                                if (app.IsExcludedFromQuickClean) current.Add(app.PackageName);
                                else current.Remove(app.PackageName);
                                await _configService.SaveExcludedPackagesAsync(current);
                            }
                        };
                        UserAppProcesses.Add(app);
                    }
                }
                foreach (var remaining in existingUsers.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in UserAppProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }
                UserAppsTotalLabel = $"{query.UserAppsTotal:F0} MB (PSS)";

                // Merge VendorOptionalProcesses
                var existingVendors = VendorOptionalProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.VendorOptional)
                {
                    if (existingVendors.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.RamMb = proc.RamMb;
                        existingVendors.Remove(proc.PackageName);
                    }
                    else
                    {
                        proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                        VendorOptionalProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingVendors.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in VendorOptionalProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }
                VendorTotalLabel = $"{query.VendorTotal:F0} MB (PSS)";

                // Merge SystemProcesses
                var existingSys = SystemProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.SystemProcesses)
                {
                    if (existingSys.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.RamMb = proc.RamMb;
                        existingSys.Remove(proc.PackageName);
                    }
                    else
                    {
                        proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                        SystemProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingSys.Values)
                {
                    remaining.RamMb = 0;
                }
                foreach (var app in SystemProcesses)
                {
                    app.IsDisabled = query.DisabledPackages.Contains(app.PackageName);
                }

                // Merge DisabledAppProcesses
                var existingDisabled = DisabledAppProcesses.ToDictionary(a => a.PackageName);
                foreach (var proc in query.DisabledAppProcesses)
                {
                    if (existingDisabled.TryGetValue(proc.PackageName, out var existing))
                    {
                        existing.IsDisabled = true;
                        existing.DisabledStateDetail = proc.DisabledStateDetail;
                        existingDisabled.Remove(proc.PackageName);
                    }
                    else
                    {
                        DisabledAppProcesses.Add(proc);
                    }
                }
                foreach (var remaining in existingDisabled.Values)
                {
                    DisabledAppProcesses.Remove(remaining);
                }
                
                DisabledCountLabel = $"DISABLED APPS ({DisabledAppProcesses.Count})";
                SystemTotalLabel = $"{query.SystemTotal:F0} MB (PSS)";

                UpdateCanForceStop();
                UpdateEstimatedReclaimable();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private async Task CleanSelectedAsync()
        {
            var selected = UserAppProcesses.Concat(VendorOptionalProcesses).Concat(SystemProcesses).Where(a => a.IsSelected && a.CanForceStop).ToList();
            if (selected.Count == 0)
            {
                _dialogService.ShowMessage("Clean Selected",
                    "No eligible apps selected.\n\nUse the checkboxes next to apps. Only apps marked as eligible can be cleaned.");
                return;
            }
            await ExecuteBatchCleanAsync(selected, "Clean Selected");
        }

        [RelayCommand]
        private async Task QuickCleanAsync()
        {
            var candidates = UserAppProcesses
                .Where(a => a.CanForceStop && !a.IsExcludedFromQuickClean && a.RamMb >= 50)
                .ToList();

            if (candidates.Count == 0)
            {
                _dialogService.ShowMessage("Quick Clean",
                    "No eligible background user apps found above 50 MB.\n\nTry scanning first, or check whether large apps appear in the User Apps list.");
                return;
            }
            await ExecuteBatchCleanAsync(candidates, "Quick Clean");
        }


        [RelayCommand]
        private async Task DisablePackageAsync(AppProcessInfo app)
        {
            if (app == null || !app.CanForceStop || app.IsCritical) return;

            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", "Device modification is disabled.\nPlease enter Advanced Mode.");
                return;
            }

            bool needsExpert = app.RiskLevel != PackageRiskLevel.LOW;
            if (needsExpert && _appModeService.CurrentMode != AppMode.Expert)
            {
                _dialogService.ShowMessage("Expert Mode Required", "You must enable Expert Actions to disable vendor/system components.");
                return;
            }

            string details = $"You are about to disable:\n\n  • {app.PackageName}\n\nThis prevents the package from running normally until re-enabled.\nRisk: {app.RecommendationString}";
            if (needsExpert) details = "⚠️ EXPERT ACTION\n\n" + details;

            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Disable Package",
                app.PackageName,
                "Enabled",
                "Disabled",
                $"adb shell pm disable-user --user 0 {app.PackageName}",
                needsExpert ? "HIGH RISK" : "MODIFY",
                details
            );

            if (!confirm) return;

            bool ok = await _memoryService.DisablePackageAsync(app.PackageName);
            _auditService.LogModification("Disable", app.PackageName, "Enabled", ok ? "Disabled" : "Failed", $"adb shell pm disable-user --user 0 {app.PackageName}", ok);
            
            if (!ok)
            {
                _dialogService.ShowMessage("Error", $"Failed to disable {app.PackageName}.");
            }
            
            await RefreshAsync();
        }

        [RelayCommand]
        private async Task EnablePackageAsync(AppProcessInfo app)
        {
            if (app == null) return;
            
            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", "Device modification is disabled.");
                return;
            }

            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Enable Package",
                app.PackageName,
                "Disabled",
                "Enabled",
                $"adb shell pm enable {app.PackageName}",
                "LOW",
                "This will restore the package to its normal enabled state."
            );

            if (!confirm) return;

            bool ok = await _memoryService.EnablePackageAsync(app.PackageName);
            _auditService.LogModification("Enable", app.PackageName, "Disabled", ok ? "Enabled" : "Failed", $"adb shell pm enable {app.PackageName}", ok);
            
            if (!ok)
            {
                _dialogService.ShowMessage("Error", $"Failed to enable {app.PackageName}.");
            }

            await RefreshAsync();
        }

        [RelayCommand]
        private async Task ForceStopAsync(AppProcessInfo app)
        {
            if (app == null || !app.CanForceStop) return;
            await ExecuteBatchCleanAsync(new List<AppProcessInfo> { app }, "Force Stop");
        }

        private async Task ExecuteBatchCleanAsync(List<AppProcessInfo> apps, string actionName)
        {
            // If any app requires Expert Actions, we must check for that.
            bool needsExpert = apps.Any(a => a.RiskLevel != PackageRiskLevel.LOW);
            
            if (needsExpert && _appModeService.CurrentMode != AppMode.Expert)
            {
                _dialogService.ShowMessage("Expert Mode Required",
                    "Some selected processes are vendor or system components.\nYou must enable Expert Actions to modify them.");
                return;
            }
            else if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied",
                    "Device modification is disabled.\nPlease enter Advanced Mode to perform this action.");
                return;
            }

            var safe = apps.Where(a => a.CanForceStop && PackageRiskEvaluator.IsValidPackageName(a.PackageName)).ToList();
            if (safe.Count == 0) return;

            double totalEstimatedMb = safe.Sum(a => a.RamMb);
            string details = $"The following applications will be force-stopped:\n\n";
            string adbCmds = "";
            foreach (var a in safe)
            {
                details += $"  • {a.PackageName}  ({a.RamMb:F0} MB  —  {a.Importance})\n";
                if (a.RiskLevel != PackageRiskLevel.LOW) {
                    details += $"    ⚠ {a.RecommendationString}: {a.Reason}\n";
                }
                adbCmds += $"adb shell am force-stop {a.PackageName}\n";
            }
            details += $"\n\nEstimated combined PSS: ~{totalEstimatedMb:F0} MB";
            
            if (needsExpert)
            {
                details = "⚠ EXPERT ACTION\n\nYou are about to modify vendor or system components. The application cannot guarantee that stopping them will be harmless.\n\n" + details;
            }

            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                needsExpert ? "EXPERT ACTION: " + actionName : actionName,
                safe.Count == 1 ? safe[0].PackageName : $"{safe.Count} applications",
                "Running / Background",
                "Force Stopped",
                adbCmds.TrimEnd(),
                needsExpert ? "HIGH RISK" : "MODIFY",
                details
            );

            if (!confirm) return;

            await _telemetryService.ForceRefreshAsync("All");
            double beforeUsedGb = CurrentMemoryInfo.UsedGB;

            int successCount = 0;
            foreach (var a in safe)
            {
                bool ok = await _memoryService.ForceStopAppAsync(a.PackageName);
                _auditService.LogModification("Force Stop", a.PackageName, a.Importance, "Stopped",
                    $"adb shell am force-stop {a.PackageName}", ok);
                if (ok) successCount++;
            }

            await Task.Delay(1500);
            await _telemetryService.ForceRefreshAsync("All");
            double afterUsedGb = CurrentMemoryInfo.UsedGB;

            double deltaMb = (beforeUsedGb - afterUsedGb) * 1024.0;
            string deltaText = deltaMb > 5 ? $"~{deltaMb:F0} MB" : "Insignificant";

            LastCleanupSummary =
                $"Last cleanup: {DateTime.Now:HH:mm}\n" +
                $"Apps stopped: {successCount}/{safe.Count}\n" +
                $"Change: {deltaText}";

            _dialogService.ShowMessage("Cleanup Complete", $"Stopped {successCount} application(s).\n\nRAM usage decreased by approximately {deltaText}.");
            await RefreshAsync();
        }
    }
}
