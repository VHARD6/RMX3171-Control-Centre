import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Services\Adb\AdbService.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Add _targetDeviceSerial and EnsureDeviceSerialAsync
new_methods = '''
        private string _targetDeviceSerial = null;
        private DateTime _lastDeviceCheck = DateTime.MinValue;

        private async Task EnsureDeviceSerialAsync()
        {
            if (DateTime.Now - _lastDeviceCheck < TimeSpan.FromSeconds(10) && !string.IsNullOrEmpty(_targetDeviceSerial))
                return;

            _lastDeviceCheck = DateTime.Now;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "devices",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var lines = output.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.EndsWith("device") && !line.StartsWith("List"))
                        {
                            var parts = line.Split(new[] { ' ', '\\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0)
                            {
                                // Prefer the physical USB device over tcp if both exist
                                if (!parts[0].Contains("._tcp"))
                                {
                                    _targetDeviceSerial = parts[0];
                                    return;
                                }
                                else if (string.IsNullOrEmpty(_targetDeviceSerial))
                                {
                                    _targetDeviceSerial = parts[0];
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
'''

# Find public async Task<bool> IsAdbAvailableAsync()
content = content.replace('        public async Task<bool> IsAdbAvailableAsync()', new_methods + '\n        public async Task<bool> IsAdbAvailableAsync()')

# Modify ExecuteCommandAsync to inject -s
exec_pattern = r'public async Task<\(string Output, string Error, int ExitCode\)> ExecuteCommandAsync\(string arguments, bool isReadOnly = true, CancellationToken cancellationToken = default\)\s*\{'
replacement = r'''public async Task<(string Output, string Error, int ExitCode)> ExecuteCommandAsync(string arguments, bool isReadOnly = true, CancellationToken cancellationToken = default)
        {
            if (!arguments.StartsWith("devices") && !arguments.StartsWith("version") && !arguments.StartsWith("start-server") && !arguments.StartsWith("kill-server") && !arguments.StartsWith("disconnect") && !arguments.StartsWith("connect"))
            {
                await EnsureDeviceSerialAsync();
                if (!string.IsNullOrEmpty(_targetDeviceSerial) && !arguments.StartsWith("-s"))
                {
                    arguments = $"-s {_targetDeviceSerial} {arguments}";
                }
            }
'''
content = re.sub(exec_pattern, replacement, content)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
