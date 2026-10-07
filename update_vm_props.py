import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_props = '''
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
'''
import re
content = re.sub(r'private string _systemTotalLabel = "0 MB \(PSS\)";', r'private string _systemTotalLabel = "0 MB (PSS)";' + new_props, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
