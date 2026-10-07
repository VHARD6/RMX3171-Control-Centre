import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Models\AppProcessInfo.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_class = '''
    public partial class CacheAppInfo : ObservableObject
    {
        public string AppName { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        
        [ObservableProperty]
        private double _cacheSizeMb;
        
        public string FormattedSize => $"{CacheSizeMb:F2} MB";
        public string Category { get; set; } = string.Empty;
        public bool IsEligible { get; set; }
        public string ExcludeReason { get; set; } = string.Empty;
    }
'''

if 'class CacheAppInfo' not in content:
    content = content.replace('namespace RMX3171ControlCentre.Models\n{', 'namespace RMX3171ControlCentre.Models\n{' + new_class)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
