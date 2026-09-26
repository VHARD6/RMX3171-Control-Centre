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

        /// <summary>
        /// User applications only — these are the cleanup candidates.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _userAppProcesses = new ObservableCollection<AppProcessInfo>();

        /// <summary>
        /// System, vendor, native daemons — read-only diagnostic view.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<AppProcessInfo> _systemProcesses = new ObservableCollection<AppProcessInfo>();

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private string _lastUpdatedText = "Press 'Scan Memory' to load.";

        [ObservableProperty]
        private string _lastCleanupSummary = "No cleanups performed this session.";

        [ObservableProperty]
        private string _userAppsTotalLabel = "0 MB (PSS)";

        [ObservableProperty]
        private string _systemTotalLabel = "0 MB (PSS)";

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

            // Subscribe to telemetry snapshots for the summary cards (lightweight — no process scan)
            _telemetryService.SnapshotUpdated += (s, e) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                });
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Scan Memory
        // ─────────────────────────────────────────────────────────────────────────
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

                // ── User apps ────────────────────────────────────────────────────
                UserAppProcesses.Clear();
                foreach (var app in query.UserApps)
                {
                    app.IsExcludedFromQuickClean = exclusions.Contains(app.PackageName);
                    app.PropertyChanged += async (s, e) =>
                    {
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
                UserAppsTotalLabel = $"{query.UserAppsTotal:F0} MB (PSS)";

                // ── System processes ─────────────────────────────────────────────
                SystemProcesses.Clear();
                foreach (var proc in query.SystemProcesses)
                    SystemProcesses.Add(proc);
                SystemTotalLabel = $"{query.SystemTotal:F0} MB (PSS)";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Clean Selected
        // ─────────────────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task CleanSelectedAsync()
        {
            var selected = UserAppProcesses.Where(a => a.IsSelected && a.CanForceStop).ToList();
            if (selected.Count == 0)
            {
                _dialogService.ShowMessage("Clean Selected",
                    "No eligible apps selected.\n\nUse the checkboxes next to apps in the USER APPS list. Only apps marked as eligible can be cleaned.");
                return;
            }
            await ExecuteBatchCleanAsync(selected, "Clean Selected");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Quick Clean
        // ─────────────────────────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────────────────────────
        // Force Stop (single row action)
        // ─────────────────────────────────────────────────────────────────────────
        [RelayCommand]
        private async Task ForceStopAsync(AppProcessInfo app)
        {
            if (app == null || !app.CanForceStop) return;
            await ExecuteBatchCleanAsync(new List<AppProcessInfo> { app }, "Force Stop");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Shared batch execution logic
        // ─────────────────────────────────────────────────────────────────────────
        private async Task ExecuteBatchCleanAsync(List<AppProcessInfo> apps, string actionName)
        {
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied",
                    $"{actionName} is a MODIFY-level operation.\nPlease enter Advanced Mode to perform this action.");
                return;
            }

            // Double-check every target passes safety
            var safe   = apps.Where(a => a.CanForceStop && PackageRiskEvaluator.IsValidPackageName(a.PackageName)).ToList();
            var unsafe_ = apps.Except(safe).ToList();

            if (safe.Count == 0)
            {
                _dialogService.ShowMessage("No eligible apps",
                    "None of the selected processes are safe to force-stop.\nOnly user-installed applications can be cleaned.");
                return;
            }

            // Build preview text
            double totalEstimatedMb = safe.Sum(a => a.RamMb);
            string details = $"The following user applications will be force-stopped:\n\n";
            string adbCmds = "";
            foreach (var a in safe)
            {
                details += $"  • {a.PackageName}  ({a.RamMb:F0} MB  —  {a.Importance})\n";
                adbCmds += $"adb shell am force-stop {a.PackageName}\n";
            }
            if (unsafe_.Any())
                details += $"\n{unsafe_.Count} item(s) skipped (protected/system/unknown).";

            details += $"\n\nEstimated combined PSS: ~{totalEstimatedMb:F0} MB";
            details += "\n\nForce stopping an app closes its processes. You may need to reopen it, and some apps may restart background services later.";
            details += "\nAndroid may reuse freed RAM for caching immediately after cleanup.";

            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                actionName,
                safe.Count == 1 ? safe[0].PackageName : $"{safe.Count} user applications",
                "Running / Background",
                "Force Stopped",
                adbCmds.TrimEnd(),
                "MODIFY",
                details
            );

            if (!confirm) return;

            // Snapshot BEFORE
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

            // Wait for OS to settle, then re-snapshot
            await Task.Delay(1500);
            await _telemetryService.ForceRefreshAsync("All");
            double afterUsedGb = CurrentMemoryInfo.UsedGB;

            double deltaMb = (beforeUsedGb - afterUsedGb) * 1024.0;
            string deltaText = deltaMb > 5
                ? $"~{deltaMb:F0} MB"
                : "Insignificant (Android may have immediately reused freed pages for caching)";

            LastCleanupSummary =
                $"Last cleanup: {DateTime.Now:HH:mm}\n" +
                $"Apps stopped: {successCount}/{safe.Count}\n" +
                $"Before: {beforeUsedGb:F2} GB\n" +
                $"After:  {afterUsedGb:F2} GB\n" +
                $"Change: {deltaText}";

            string resultMsg =
                $"Stopped {successCount} application(s).\n\n" +
                $"RAM usage decreased by approximately {deltaText}.\n\n" +
                "Android may reuse available RAM for caching immediately after cleanup.\n" +
                "Some apps may restart their background services automatically.";

            _dialogService.ShowMessage("Cleanup Complete", resultMsg);
            await RefreshAsync();
        }
    }
}
