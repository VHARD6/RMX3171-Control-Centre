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

        [ObservableProperty]
        private ObservableCollection<AppPackageInfo> _packages = new ObservableCollection<AppPackageInfo>();

        [ObservableProperty]
        private bool _isLoading;

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

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var pkgs = await _appManagerService.GetInstalledPackagesAsync();
                Packages.Clear();
                foreach (var pkg in pkgs)
                {
                    Packages.Add(pkg);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task DisableAppAsync(AppPackageInfo app)
        {
            if (app == null) return;
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Disable App is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell pm disable-user --user 0 {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Disable App", app.PackageName, "Enabled", "Disabled", adbCmd, "MODIFY", "Disabling system apps may cause device instability. The app will be hidden and stopped.");
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
            if (!_appModeService.IsModificationAllowed(RiskLevel.Modify))
            {
                _dialogService.ShowMessage("Access Denied", "Uninstall is a MODIFY operation. Enter Advanced Mode.");
                return;
            }

            string adbCmd = $"adb shell pm uninstall -k --user 0 {app.PackageName}";
            bool confirm = await _dialogService.ShowModificationPreviewAsync("Uninstall App (User 0)", app.PackageName, "Installed", "Uninstalled", adbCmd, "MODIFY", "The app will be uninstalled for the current user. Data may be kept. Factory reset restores system apps.");
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
                if (success) { _dialogService.ShowMessage("Success", "App Stopped."); await RefreshAsync(); }
            }
        }
    }
}
