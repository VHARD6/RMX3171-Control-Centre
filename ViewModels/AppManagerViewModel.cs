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
        private readonly IAppModeService _appModeService;
        private readonly IAuditService _auditService;
        private readonly IDialogService _dialogService;

        private List<AppPackageInfo> _allPackages = new List<AppPackageInfo>();

        [ObservableProperty]
        private ObservableCollection<AppPackageInfo> _filteredPackages = new ObservableCollection<AppPackageInfo>();

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _searchQuery = "";

        [ObservableProperty]
        private string _selectedFilter = "All";
        public ObservableCollection<string> Filters { get; } = new ObservableCollection<string> { "All", "User Apps", "System Apps", "Disabled", "Protected" };

        [ObservableProperty]
        private AppPackageInfo? _selectedApp;
        
        [ObservableProperty]
        private bool _isAppSelected;

        public AppManagerViewModel(
            IAppManagerService appManagerService,
            IMemoryService memoryService,
            IAppModeService appModeService,
            IAuditService auditService,
            IDialogService dialogService)
        {
            _appManagerService = appManagerService;
            _memoryService = memoryService;
            _appModeService = appModeService;
            _auditService = auditService;
            _dialogService = dialogService;
        }

        partial void OnSearchQueryChanged(string value) => ApplyFilters();
        partial void OnSelectedFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedAppChanged(AppPackageInfo? value)
        {
            IsAppSelected = value != null;
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var pkgs = await _appManagerService.GetInstalledPackagesAsync();
                _allPackages = pkgs;
                ApplyFilters();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilters()
        {
            var query = SearchQuery?.ToLowerInvariant() ?? "";
            
            var filtered = _allPackages.Where(p => 
                (string.IsNullOrWhiteSpace(query) || p.PackageName.ToLowerInvariant().Contains(query) || p.AppName.ToLowerInvariant().Contains(query))
            );

            filtered = SelectedFilter switch
            {
                "User Apps" => filtered.Where(p => p.IsUserApp),
                "System Apps" => filtered.Where(p => p.IsSystem),
                "Disabled" => filtered.Where(p => !p.IsEnabled),
                "Protected" => filtered.Where(p => p.IsProtected),
                _ => filtered
            };

            FilteredPackages.Clear();
            foreach (var f in filtered)
            {
                FilteredPackages.Add(f);
            }
        }

        [RelayCommand]
        private async Task DisableAppAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (app.RiskLevel == PackageRiskLevel.PROTECTED || app.RiskLevel == PackageRiskLevel.UNKNOWN)
            {
                _dialogService.ShowMessage("Cannot Disable", "This package is protected or unknown. Disabling it is unsafe.");
                return;
            }
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Disable App is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell pm disable-user --user 0 {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Disable App", app.PackageName, "Enabled", "Disabled", adbCmd, "MODIFY", "Prevents the package from running without removing it.");
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
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Enable App is a MODIFY operation. Enter Advanced Mode.");
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
            if (app.RiskLevel == PackageRiskLevel.PROTECTED || app.RiskLevel == PackageRiskLevel.UNKNOWN || app.RiskLevel == PackageRiskLevel.SYSTEM)
            {
                _dialogService.ShowMessage("Cannot Uninstall", "This package is a system or protected app. You cannot cleanly uninstall it for the user without risk.");
                return;
            }
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Uninstall is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell pm uninstall --user 0 {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Uninstall for User", app.PackageName, "Installed", "Uninstalled", adbCmd, "HIGH RISK", "Removes the package for Android user 0 where supported.\n\nThis removes the application for the selected Android user but may leave system components/files behind. Reinstall/restore may require additional steps.");
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
            if (app.RiskLevel == PackageRiskLevel.PROTECTED || app.RiskLevel == PackageRiskLevel.UNKNOWN)
            {
                _dialogService.ShowMessage("Cannot Force Stop", "This package is protected or unknown. Stopping it is unsafe.");
                return;
            }
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Force Stop is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell am force-stop {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Force Stop App", app.PackageName, "Running", "Stopped", adbCmd, "MODIFY", "App will be stopped.");
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
                _dialogService.ShowMessage("Action Plan", "No applications selected. Use the checkboxes to select user applications.");
                return;
            }

            var invalid = selected.Where(p => p.RiskLevel == PackageRiskLevel.PROTECTED || p.RiskLevel == PackageRiskLevel.UNKNOWN).ToList();
            if (invalid.Any())
            {
                _dialogService.ShowMessage("Cannot Proceed", $"You have selected protected/unknown applications ({invalid.Count}). Uncheck them to proceed.");
                return;
            }

            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Executing an Action Plan is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            // We just do a simple summary logic for this step.
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
                "MODIFY",
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
