using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.Services.Device
{
    public enum TransportType
    {
        Unknown,
        Usb,
        WiFi
    }

    public enum ConnectionState
    {
        NO_DEVICE,
        OFFLINE,
        UNAUTHORIZED,
        AUTHORIZED,
        CONNECTED_USB,
        CONNECTED_WIFI,
        CONNECTED_UNKNOWN
    }

    public class AdbDeviceInfo
    {
        public string SerialOrAddress { get; set; } = "";
        public string AdbState { get; set; } = "";
        public TransportType Transport { get; set; } = TransportType.Unknown;
        public Dictionary<string, string> Properties { get; set; } = new();
    }

    public interface IDeviceService
    {
        Task<ConnectionState> GetConnectionStateAsync();
        Task<Dictionary<string, string>> GetDevicePropertiesAsync();
        Task<string> GetStorageInfoAsync();
        Task<string> GetBatteryInfoAsync();
        Task<(string Output, bool Success)> PairDeviceAsync(string ip, string port, string code);
        Task<(string Output, bool Success)> ConnectDeviceAsync(string ip, string port);
        Task<(string Output, bool Success)> DisconnectDeviceAsync();
    }

    public class DeviceService : IDeviceService
    {
        private readonly IAdbService _adbService;
        private readonly ILogService _logService;

        private string? _lastLoggedSerial;
        private string? _lastLoggedTransport;
        private string? _lastLoggedAdbState;

        public DeviceService(IAdbService adbService, ILogService logService)
        {
            _adbService = adbService;
            _logService = logService;
        }

        public async Task<ConnectionState> GetConnectionStateAsync()
        {
            var result = await _adbService.ExecuteCommandAsync("devices -l");
            if (result.ExitCode != 0)
            {
                LogDetectionIfChanged("none", "None", "no device");
                return ConnectionState.NO_DEVICE;
            }

            var devices = ParseDevicesOutput(result.Output);
            if (devices.Count == 0)
            {
                LogDetectionIfChanged("none", "None", "no device");
                return ConnectionState.NO_DEVICE;
            }

            // Find authorized/connected devices first
            var connectedDevices = devices.FindAll(d => d.AdbState.Equals("device", StringComparison.OrdinalIgnoreCase));
            if (connectedDevices.Count > 0)
            {
                // If a USB device is attached, classify as USB; otherwise if Wi-Fi is attached, classify as Wi-Fi
                var activeDevice = connectedDevices.Find(d => d.Transport == TransportType.Usb)
                                   ?? connectedDevices.Find(d => d.Transport == TransportType.WiFi)
                                   ?? connectedDevices[0];

                string transportStr = activeDevice.Transport switch
                {
                    TransportType.Usb => "USB",
                    TransportType.WiFi => "Wi-Fi",
                    _ => "Unknown"
                };

                LogDetectionIfChanged(activeDevice.SerialOrAddress, transportStr, activeDevice.AdbState);

                return activeDevice.Transport switch
                {
                    TransportType.Usb => ConnectionState.CONNECTED_USB,
                    TransportType.WiFi => ConnectionState.CONNECTED_WIFI,
                    _ => ConnectionState.CONNECTED_UNKNOWN
                };
            }

            // Check if any device is unauthorized
            var unauthDevice = devices.Find(d => d.AdbState.Equals("unauthorized", StringComparison.OrdinalIgnoreCase));
            if (unauthDevice != null)
            {
                string transportStr = unauthDevice.Transport switch
                {
                    TransportType.Usb => "USB",
                    TransportType.WiFi => "Wi-Fi",
                    _ => "Unknown"
                };
                LogDetectionIfChanged(unauthDevice.SerialOrAddress, transportStr, unauthDevice.AdbState);
                return ConnectionState.UNAUTHORIZED;
            }

            // Check if any device is offline
            var offlineDevice = devices.Find(d => d.AdbState.Equals("offline", StringComparison.OrdinalIgnoreCase));
            if (offlineDevice != null)
            {
                string transportStr = offlineDevice.Transport switch
                {
                    TransportType.Usb => "USB",
                    TransportType.WiFi => "Wi-Fi",
                    _ => "Unknown"
                };
                LogDetectionIfChanged(offlineDevice.SerialOrAddress, transportStr, offlineDevice.AdbState);
                return ConnectionState.OFFLINE;
            }

            var firstDev = devices[0];
            string fallbackTransport = firstDev.Transport switch
            {
                TransportType.Usb => "USB",
                TransportType.WiFi => "Wi-Fi",
                _ => "Unknown"
            };
            LogDetectionIfChanged(firstDev.SerialOrAddress, fallbackTransport, firstDev.AdbState);
            return ConnectionState.NO_DEVICE;
        }

        private void LogDetectionIfChanged(string serial, string transport, string adbState)
        {
            if (serial != _lastLoggedSerial || transport != _lastLoggedTransport || adbState != _lastLoggedAdbState)
            {
                _lastLoggedSerial = serial;
                _lastLoggedTransport = transport;
                _lastLoggedAdbState = adbState;

                var logText = $"ADB serial/address: {serial}\nTransport: {transport}\nADB state: {adbState}";
                _logService.LogDebug(logText);
            }
        }

        public static List<AdbDeviceInfo> ParseDevicesOutput(string output)
        {
            var devices = new List<AdbDeviceInfo>();
            if (string.IsNullOrWhiteSpace(output)) return devices;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("List of devices attached", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = Regex.Split(line, @"\s+");
                if (parts.Length < 2) continue;

                var serialOrAddress = parts[0];
                var adbState = parts[1];

                var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 2; i < parts.Length; i++)
                {
                    var p = parts[i];
                    int colonIndex = p.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = p.Substring(0, colonIndex);
                        var val = p.Substring(colonIndex + 1);
                        props[key] = val;
                    }
                }

                var transport = DetermineTransport(serialOrAddress, line, parts, props);

                devices.Add(new AdbDeviceInfo
                {
                    SerialOrAddress = serialOrAddress,
                    AdbState = adbState,
                    Transport = transport,
                    Properties = props
                });
            }

            return devices;
        }

        public static TransportType DetermineTransport(string serialOrAddress, string line, string[] tokens, Dictionary<string, string> props)
        {
            // 1. Explicit USB transport indicator from ADB
            if (IsExplicitUsb(line, tokens, props))
            {
                return TransportType.Usb;
            }

            // 2. Network endpoint (IPv4:port, IPv6, or mDNS wireless service)
            if (IsNetworkEndpoint(serialOrAddress))
            {
                return TransportType.WiFi;
            }

            // 3. Fallback: Cannot be determined reliably
            return TransportType.Unknown;
        }

        private static bool IsExplicitUsb(string line, string[] tokens, Dictionary<string, string> props)
        {
            if (props.ContainsKey("usb")) return true;
            if (tokens.Any(t => t.StartsWith("usb:", StringComparison.OrdinalIgnoreCase))) return true;
            if (Regex.IsMatch(line, @"\busb:\S+", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static bool IsNetworkEndpoint(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return false;

            // Android 11+ mDNS TLS/TCP service names:
            // e.g. adb-WOBID6KFGALJ59CY-FSgEY2._adb-tls-connect._tcp
            // or adb-xxx._adb._tcp
            if (identifier.Contains("._adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase) ||
                identifier.Contains("._adb._tcp", StringComparison.OrdinalIgnoreCase) ||
                identifier.EndsWith("._tcp", StringComparison.OrdinalIgnoreCase) ||
                identifier.Contains("._tcp.", StringComparison.OrdinalIgnoreCase) ||
                identifier.Contains("_adb-tls-connect", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // IPv4:port (e.g. 192.168.1.42:37123)
            if (Regex.IsMatch(identifier, @"^(?:\d{1,3}\.){3}\d{1,3}:\d+$"))
            {
                return true;
            }

            // Bracketed IPv6 with port (e.g. [fe80::1]:5555 or [fe80::1%wlan0]:37123)
            if (Regex.IsMatch(identifier, @"^\[[0-9a-fA-F:]+(?:%[a-zA-Z0-9_-]+)?\]:\d+$"))
            {
                return true;
            }

            // Raw IPv6 endpoint with at least two colons
            if (identifier.Contains("::") && identifier.Contains(":"))
            {
                return true;
            }

            // General host:port endpoint where port is numeric (e.g. localhost:5555, device.lan:37123)
            if (Regex.IsMatch(identifier, @"^[^:\s]+:\d+$"))
            {
                return true;
            }

            return false;
        }

        public async Task<(string Output, bool Success)> PairDeviceAsync(string ip, string port, string code)
        {
            var result = await _adbService.ExecuteCommandAsync($"pair {ip}:{port} {code}", isReadOnly: true);
            return (result.Output.Trim(), result.ExitCode == 0);
        }

        public async Task<(string Output, bool Success)> ConnectDeviceAsync(string ip, string port)
        {
            var result = await _adbService.ExecuteCommandAsync($"connect {ip}:{port}", isReadOnly: true);
            return (result.Output.Trim(), result.ExitCode == 0);
        }

        public async Task<(string Output, bool Success)> DisconnectDeviceAsync()
        {
            var result = await _adbService.ExecuteCommandAsync($"disconnect", isReadOnly: true);
            return (result.Output.Trim(), result.ExitCode == 0);
        }

        public async Task<Dictionary<string, string>> GetDevicePropertiesAsync()
        {
            var props = new Dictionary<string, string>();
            var result = await _adbService.ExecuteCommandAsync("shell getprop");
            
            if (result.ExitCode != 0) return props;

            var regex = new Regex(@"\[(.*?)\]:\s*\[(.*?)\]");
            var matches = regex.Matches(result.Output);

            foreach (Match match in matches)
            {
                if (match.Groups.Count == 3)
                {
                    props[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }

            return props;
        }

        public async Task<string> GetStorageInfoAsync()
        {
            var result = await _adbService.ExecuteCommandAsync("shell df -h");
            return result.ExitCode == 0 ? result.Output : "Unable to retrieve storage info.";
        }

        public async Task<string> GetBatteryInfoAsync()
        {
            var result = await _adbService.ExecuteCommandAsync("shell dumpsys battery");
            return result.ExitCode == 0 ? result.Output : "Unable to retrieve battery info.";
        }
    }
}
