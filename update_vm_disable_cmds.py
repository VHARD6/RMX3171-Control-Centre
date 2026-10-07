import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_commands = '''
        [RelayCommand]
        private async Task DisablePackageAsync(AppProcessInfo app)
        {
            if (app == null || !app.CanForceStop || app.IsCritical) return;

            if (_appModeService.CurrentMode == AppMode.ReadOnly)
            {
                _dialogService.ShowMessage("Access Denied", "Device modification is disabled.\\nPlease enter Advanced Mode.");
                return;
            }

            bool needsExpert = app.RiskLevel != PackageRiskLevel.LOW;
            if (needsExpert && _appModeService.CurrentMode != AppMode.Expert)
            {
                _dialogService.ShowMessage("Expert Mode Required", "You must enable Expert Actions to disable vendor/system components.");
                return;
            }

            string details = $"You are about to disable:\\n\\n  • {app.PackageName}\\n\\nThis prevents the package from running normally until re-enabled.\\nRisk: {app.RecommendationString}";
            if (needsExpert) details = "⚠️ EXPERT ACTION\\n\\n" + details;

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
'''

content = content.replace('        [RelayCommand]\n        private async Task ForceStopAsync(AppProcessInfo app)', new_commands + '\n        [RelayCommand]\n        private async Task ForceStopAsync(AppProcessInfo app)')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
