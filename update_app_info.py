import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Models\AppProcessInfo.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

new_props = '''
        private string _disabledStateDetail = string.Empty;
        public string DisabledStateDetail
        {
            get => _disabledStateDetail;
            set
            {
                SetProperty(ref _disabledStateDetail, value);
                OnPropertyChanged(nameof(StateText));
            }
        }

        public string StateText 
        {
            get 
            {
                if (IsDisabled) 
                {
                    return string.IsNullOrEmpty(DisabledStateDetail) ? "DISABLED" : DisabledStateDetail;
                }
                if (RamMb > 0) return "RUNNING";
                return "STOPPED";
            }
        }
'''

# replace StateText property
old_state_text_pattern = re.compile(r'public string StateText\s*\{\s*get\s*\{\s*if \(IsDisabled\) return "DISABLED";\s*if \(RamMb > 0\) return "RUNNING";\s*return "STOPPED";\s*\}\s*\}', re.MULTILINE)
content = old_state_text_pattern.sub(new_props, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
