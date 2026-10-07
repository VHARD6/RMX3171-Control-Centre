import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Models\AppProcessInfo.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_props = '''
        private double _ramMb;
        public double RamMb 
        { 
            get => _ramMb; 
            set 
            {
                SetProperty(ref _ramMb, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        private bool _isDisabled;
        public bool IsDisabled
        {
            get => _isDisabled;
            set
            {
                SetProperty(ref _isDisabled, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText 
        {
            get 
            {
                if (IsDisabled) return "DISABLED";
                if (RamMb > 0) return "RUNNING";
                return "STOPPED";
            }
        }
'''

content = content.replace('public double RamMb { get; set; }', new_props)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
