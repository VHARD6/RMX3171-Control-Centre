import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\ViewModels\StorageViewModel.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

hook = '''
            _cleanupOpportunitiesViewModel.RequestViewChange += (vm) =>
            {
                IsHubVisible = false;
                CurrentView = vm;
            };
'''
content = content.replace('_cleanupOpportunitiesViewModel.OnBackRequested += () =>\n            {\n                IsHubVisible = true;\n                CurrentView = null;\n            };', '_cleanupOpportunitiesViewModel.OnBackRequested += () =>\n            {\n                IsHubVisible = true;\n                CurrentView = null;\n            };\n' + hook)

with open(path, 'w', encoding='utf-8-sig') as f:
    f.write(content)
