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
    public partial class AppManagerViewModel : ViewModelBase
    {
        private readonly IAppManagerService _appManagerService;
        private readonly IMemoryService _memoryService;
        private readonly IDialogService _dialogService;
        private readonly IAppModeService _appModeService;
        private readonly IAuditService _auditService;

        private List<AppPackageInfo> _allPackages = new List<AppPackageInfo>();

        [ObservableProperty]
        private ObservableCollection<AppPackageInfo> _filteredPackages = new ObservableCollection<AppPackageInfo>();

        [ObservableProperty]
        private AppPackageInfo? _selectedApp;

        [ObservableProperty]
        private bool _isRefreshing;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "All";

        [ObservableProperty]
        private ObservableCollection<string> _availableFilters = new ObservableCollection<string>
        {
            "All", "User Apps", "Vendor/OEM", "Google", "System", "Cleanup Candidates", "Optional", "Protected", "Critical", "Unknown"
        };

        public bool IsAppSelected => SelectedApp != null;

        public AppManagerViewModel(
            IAppManagerService appManagerService,
            IMemoryService memoryService,
            IDialogService dialogService,
            IAppModeService appModeService,
            IAuditService auditService)
        {
            _appManagerService = appManagerService;
            _memoryService = memoryService;
            _dialogService = dialogService;
            _appModeService = appModeService;
            _auditService = auditService;
        }

        partial void OnSelectedAppChanged(AppPackageInfo? value)
        {
            OnPropertyChanged(nameof(IsAppSelected));
        }

        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            ApplyFilter();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                _allPackages = await _appManagerService.GetInstalledPackagesAsync();
                ApplyFilter();
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private void ApplyFilter()
        {
            var query = _allPackages.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var lowerSearch = SearchQuery.ToLowerInvariant();
                query = query.Where(p => 
                    (p.PackageName != null && p.PackageName.ToLowerInvariant().Contains(lowerSearch)) ||
                    (p.AppName != null && p.AppName.ToLowerInvariant().Contains(lowerSearch)));
            }

            switch (SelectedFilter)
            {
                case "User Apps":
                    query = query.Where(p => p.IsUserApp);
                    break;
                case "Vendor/OEM":
                    query = query.Where(p => p.Recommendation == PackageRecommendation.OPTIONAL_COMPONENT || p.RiskLevel == PackageRiskLevel.MODERATE);
                    break;
                case "Google":
                    query = query.Where(p => p.PackageName.StartsWith("com.google.", StringComparison.OrdinalIgnoreCase) || p.PackageName.StartsWith("com.android.vending", StringComparison.OrdinalIgnoreCase));
                    break;
                case "System":
                    query = query.Where(p => p.IsSystem);
                    break;
                case "Cleanup Candidates":
                    query = query.Where(p => p.Recommendation == PackageRecommendation.REMOVE_CANDIDATE);
                    break;
                case "Optional":
                    query = query.Where(p => p.Recommendation == PackageRecommendation.OPTIONAL_COMPONENT);
                    break;
                case "Protected":
                    query = query.Where(p => p.Recommendation == PackageRecommendation.DO_NOT_REMOVE);
                    break;
                case "Critical":
                    query = query.Where(p => p.IsCritical);
                    break;
                case "Unknown":
                    query = query.Where(p => p.RiskLevel == PackageRiskLevel.UNKNOWN);
                    break;
            }

            var filtered = query.ToList();
            FilteredPackages.Clear();
            foreach (var f in filtered)
            {
                FilteredPackages.Add(f);
            }
        }

        private bool CheckModificationPermission(AppPackageInfo app, string actionName)
        {
            if (app.IsCritical)
            {
                _dialogService.ShowMessage("Cannot Proceed", $"This package is marked as CRITICAL. {actionName} is unsafe and blocked.");
                return false;
            }

            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", $"{actionName} requires device modification features. Please enter Advanced Mode.");
                return false;
            }

            if (app.RiskLevel != PackageRiskLevel.LOW && _appModeService.CurrentMode != AppMode.Expert)
            {
                _dialogService.ShowMessage("Expert Mode Required", $"This package is classified as {app.RiskLevel}. Modifying it requires Expert Actions to be enabled.");
                return false;
            }

            return true;
        }

        [RelayCommand]
        private async Task DisableAppAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (!CheckModificationPermission(app, "Disable App")) return;

            string adbCmd = $"adb shell pm disable-user --user 0 {app.PackageName}";
            bool isExpert = app.RiskLevel != PackageRiskLevel.LOW;
            string details = "Prevents the package from running without removing it.\n\n";
            if (isExpert) details = $"⚠ EXPERT ACTION\n\nYou are modifying a {app.RiskLevel} package. {app.Reason}\n\n" + details;

            bool confirm = await _dialogService.ShowModificationPreviewAsync("Disable App", app.PackageName, "Enabled", "Disabled", adbCmd, isExpert ? "HIGH RISK" : "MODIFY", details);
            if (confirm)
            {
                bool success = await _appManagerService.DisableAppAsync(app.PackageName);
                _auditService.LogModification("Disable App", app.PackageName, "Enabled", "Disabled", adbCmd, success);
                if (success) { _dialogService.ShowMessage("Success", "App Disabled."); await RefreshAsync(); }
            }
        }

        [RelayCommand]
        private async Task EnableAppAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", "Enable App requires device modification features. Please enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell pm enable {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Enable App", app.PackageName, "Disabled", "Enabled", adbCmd, "MODIFY", "The app will be restored to an active state.");
            if (confirm)
            {
                bool success = await _appManagerService.EnableAppAsync(app.PackageName);
                _auditService.LogModification("Enable App", app.PackageName, "Disabled", "Enabled", adbCmd, success);
                if (success) { _dialogService.ShowMessage("Success", "App Enabled."); await RefreshAsync(); }
            }
        }

        [RelayCommand]
        private async Task UninstallUserAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (!CheckModificationPermission(app, "Uninstall for User")) return;

            string adbCmd = $"adb shell pm uninstall --user 0 {app.PackageName}";
            bool isExpert = app.RiskLevel != PackageRiskLevel.LOW;
            
            string details = "Removes the package from Android user 0 where supported. Reinstallation may require restoring the package.\n\n";
            if (isExpert) details = $"⚠ EXPERT ACTION\n\nYou are removing a {app.RiskLevel} package. {app.Reason}\n\n" + details;

            bool confirm = await _dialogService.ShowModificationPreviewAsync("Uninstall for User", app.PackageName, "Installed", "Uninstalled", adbCmd, isExpert ? "HIGH RISK" : "MODIFY", details);
            if (confirm)
            {
                bool success = await _appManagerService.UninstallAppForUserAsync(app.PackageName);
                _auditService.LogModification("Uninstall", app.PackageName, "Installed", "Uninstalled", adbCmd, success);
                if (success) { _dialogService.ShowMessage("Success", "App Uninstalled for User 0."); await RefreshAsync(); }
            }
        }

        [RelayCommand]
        private async Task ForceStopAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (!CheckModificationPermission(app, "Force Stop App")) return;

            string adbCmd = $"adb shell am force-stop {app.PackageName}";
            bool isExpert = app.RiskLevel != PackageRiskLevel.LOW;
            
            string details = "App will be stopped.\n\n";
            if (isExpert) details = $"⚠ EXPERT ACTION\n\nYou are modifying a {app.RiskLevel} package. {app.Reason}\n\n" + details;

            bool confirm = await _dialogService.ShowModificationPreviewAsync("Force Stop App", app.PackageName, "Running", "Stopped", adbCmd, isExpert ? "HIGH RISK" : "MODIFY", details);
            if (confirm)
            {
                bool success = await _memoryService.ForceStopAppAsync(app.PackageName);
                _auditService.LogModification("Force Stop", app.PackageName, "Running", "Stopped", adbCmd, success);
                if (success) 
                { 
                    _dialogService.ShowMessage("Success", "App Stopped."); 
                    await RefreshAsync(); 
                }
            }
        }

        [RelayCommand]
        private async Task BuildActionPlanAsync()
        {
            var selected = _allPackages.Where(p => p.IsSelectedForAction).ToList();
            if (selected.Count == 0)
            {
                _dialogService.ShowMessage("Action Plan", "No applications selected. Use the checkboxes to select applications.");
                return;
            }

            var critical = selected.Where(p => p.IsCritical).ToList();
            if (critical.Any())
            {
                _dialogService.ShowMessage("Cannot Proceed", $"You have selected {critical.Count} CRITICAL applications. Uncheck them to proceed.");
                return;
            }

            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", "Executing an Action Plan requires device modification. Enter Advanced Mode.");
                return;
            }

            var nonLow = selected.Where(p => p.RiskLevel != PackageRiskLevel.LOW).ToList();
            if (nonLow.Any() && _appModeService.CurrentMode != AppMode.Expert)
            {
                _dialogService.ShowMessage("Expert Mode Required", $"You have selected {nonLow.Count} system/vendor components. Enable Expert Actions to modify them.");
                return;
            }

            string details = $"ACTION PLAN\n\n{selected.Count} applications selected.\n\n";
            string adbCommands = "";
            foreach (var app in selected)
            {
                details += $"Force Stop: {app.PackageName}\n";
                adbCommands += $"adb shell am force-stop {app.PackageName}\n";
            }
            details += "\nNothing has been changed yet.";

            bool confirm = await _dialogService.ShowModificationPreviewAsync(
                "Execute Action Plan",
                "Multiple Applications",
                "Running/Enabled",
                "Stopped",
                adbCommands.TrimEnd(),
                nonLow.Any() ? "HIGH RISK" : "MODIFY",
                details
            );

            if (confirm)
            {
                int successCount = 0;
                foreach (var app in selected)
                {
                    bool success = await _memoryService.ForceStopAppAsync(app.PackageName);
                    _auditService.LogModification("Force Stop", app.PackageName, "Running", "Stopped", $"adb shell am force-stop {app.PackageName}", success);
                    if (success) successCount++;
                }

                _dialogService.ShowMessage("Action Plan Complete", $"Successfully processed {successCount} of {selected.Count} applications.");
                await RefreshAsync();
            }
        }
    }
}
