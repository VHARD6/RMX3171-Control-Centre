import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\MemoryManagerViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# 1. Add new properties and methods
new_props = '''        [ObservableProperty]
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
content = content.replace('        [ObservableProperty]\n        private string _dataSources = "";', new_props + '\n        [ObservableProperty]\n        private string _dataSources = "";')

# 2. Add property changed hook for UpdateEstimatedReclaimable
hook = '''                        if (e.PropertyName == nameof(AppProcessInfo.IsSelected))
                        {
                            UpdateEstimatedReclaimable();
                        }'''
content = content.replace('if (e.PropertyName == nameof(AppProcessInfo.IsExcludedFromQuickClean))', hook + '\n                        if (e.PropertyName == nameof(AppProcessInfo.IsExcludedFromQuickClean))')

# add for vendor
vendor_hook = '''                    proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                    VendorOptionalProcesses.Add(proc);'''
content = content.replace('VendorOptionalProcesses.Add(proc);', vendor_hook)

# add for system
system_hook = '''                    proc.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(AppProcessInfo.IsSelected)) UpdateEstimatedReclaimable(); };
                    SystemProcesses.Add(proc);'''
content = content.replace('SystemProcesses.Add(proc);', system_hook)

# Call UpdateEstimatedReclaimable at the end of load
content = content.replace('UpdateCanForceStop();', 'UpdateCanForceStop();\n                UpdateEstimatedReclaimable();')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
