import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\Storage\CleanupOpportunitiesViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add usings
usings = '''using RMX3171ControlCentre.Services.Security;
using RMX3171ControlCentre.Services.UI;
using RMX3171ControlCentre.Services.Adb;
'''
content = content.replace('using RMX3171ControlCentre.Models.Storage;', 'using RMX3171ControlCentre.Models.Storage;\n' + usings)

# Add dependencies and constructor
dependencies = '''
        private readonly IAppModeService _appModeService;
        private readonly IDialogService _dialogService;
        private readonly IAuditService _auditService;
        private readonly IAdbService _adbService;

        public CleanupOpportunitiesViewModel(
            IAppModeService appModeService,
            IDialogService dialogService,
            IAuditService auditService,
            IAdbService adbService)
        {
            _appModeService = appModeService;
            _dialogService = dialogService;
            _auditService = auditService;
            _adbService = adbService;
            
            _appModeService.ModeChanged += (s, e) => 
            {
                OnPropertyChanged(nameof(CanDelete));
            };
        }

        public bool CanDelete => _appModeService.CurrentMode == RMX3171ControlCentre.Models.AppMode.Advanced || 
                                 _appModeService.CurrentMode == RMX3171ControlCentre.Models.AppMode.Expert;
'''
content = content.replace('public event Action<ViewModelBase>? RequestViewChange;', dependencies + '\n        public event Action<ViewModelBase>? RequestViewChange;')

# Format size helper
format_size = '''
        private string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }
'''

# Add DeleteSelectedCommand
delete_cmd = '''
        [RelayCommand]
        private async Task DeleteSelectedAsync()
        {
            if (!CanDelete)
            {
                _dialogService.ShowMessage("Access Denied", "Deletion requires Advanced Mode.");
                return;
            }

            var selected = Candidates.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                _dialogService.ShowMessage("No Selection", "Please select items to delete.");
                return;
            }

            long totalBytes = selected.Sum(x => x.SizeBytes);
            string formattedTotal = FormatSize(totalBytes);
            
            // Build summary string for the dialog
            string targetList = selected.Count == 1 
                ? $"1 item: {selected[0].FileName}" 
                : $"{selected.Count} items (Total: {formattedTotal})";
                
            string stateList = string.Join("\n", selected.Select(x => $"- {x.FullPath} ({x.SizeText})"));
            
            // Check for unsafe paths
            var unsafePaths = new[] { "/system", "/vendor", "/product", "/data/system", "/sbin" };
            foreach (var item in selected)
            {
                if (unsafePaths.Any(p => item.FullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _dialogService.ShowMessage("Safety Violation", $"Cannot delete protected system path:\n{item.FullPath}");
                    return;
                }
            }

            // Verify with audit UI
            var firstItem = selected[0];
            string adbCmd = selected.Count == 1 
                ? $"rm \"{firstItem.FullPath}\"" 
                : $"rm ... ({selected.Count} items)";

            bool confirmed = await _dialogService.ShowModificationPreviewAsync(
                "Delete Files",
                targetList,
                stateList,
                "Delete permanently from storage",
                adbCmd,
                "HIGH (Irreversible)",
                "Files will be completely removed. This action cannot be undone."
            );

            if (!confirmed) return;

            IsAnalyzing = true;
            AnalysisStatusText = $"Deleting {selected.Count} items...";

            try
            {
                int successCount = 0;
                int failCount = 0;

                foreach (var item in selected.ToList())
                {
                    // Using AdbService directly with isReadOnly: false
                    var cmd = $"rm \"{item.FullPath}\"";
                    var result = await _adbService.ExecuteCommandAsync(cmd, false);
                    
                    if (result.ExitCode == 0 && !result.Error.Contains("Permission denied") && !result.Error.Contains("No such file"))
                    {
                        successCount++;
                        _auditService.LogModification("Delete", item.FullPath, "Exists", "Deleted", cmd, true);
                        
                        // Remove from Candidates UI
                        Candidates.Remove(item);
                        
                        // Optionally remove from the underlying VMs if present
                        if (_photosVm != null) _photosVm.AllPhotos.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_videosVm != null) _videosVm.AllVideos.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_docsVm != null) _docsVm.AllDocuments.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_downVm != null) _downVm.AllDownloads.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_apksVm != null) _apksVm.AllApks.RemoveAll(x => x.FullPath == item.FullPath);
                        if (_otherVm != null) _otherVm.AllOtherItems.RemoveAll(x => x.FullPath == item.FullPath);
                    }
                    else
                    {
                        failCount++;
                        _auditService.LogModification("Delete", item.FullPath, "Exists", "Failed", cmd, false);
                        System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                            _dialogService.ShowMessage("Deletion Failed", $"Failed to delete {item.FileName}.\nError: {result.Error}\nOutput: {result.Output}");
                        });
                    }
                }
                
                AnalysisStatusText = $"Deletion complete. {successCount} succeeded, {failCount} failed.";
            }
            catch (Exception ex)
            {
                AnalysisStatusText = $"Deletion error: {ex.Message}";
                _dialogService.ShowMessage("Error", ex.Message);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }
'''
content = content.replace('private void GoBack()\n        {\n            OnBackRequested?.Invoke();\n        }', 'private void GoBack()\n        {\n            OnBackRequested?.Invoke();\n        }\n' + format_size + delete_cmd)

with open(path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
