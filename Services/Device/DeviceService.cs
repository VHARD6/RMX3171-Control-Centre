using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.Services.Device
{
    public enum ConnectionState
    {
        NO_DEVICE,
        OFFLINE,
        UNAUTHORIZED,
        AUTHORIZED,
        CONNECTED_USB,
        CONNECTED_WIFI
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

        public DeviceService(IAdbService adbService)
        {
            _adbService = adbService;
        }

        public async Task<ConnectionState> GetConnectionStateAsync()
        {
            var result = await _adbService.ExecuteCommandAsync("devices -l");
            if (result.ExitCode != 0) return ConnectionState.NO_DEVICE;

            var lines = result.Output.Split('\n');
            bool foundDevice = false;
            ConnectionState state = ConnectionState.NO_DEVICE;

            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices attached")) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                foundDevice = true;
                if (line.Contains("unauthorized")) return ConnectionState.UNAUTHORIZED;
                if (line.Contains("offline")) return ConnectionState.OFFLINE;
                
                if (line.Contains("device"))
                {
                    // If it contains an IP:port or specifically looks like TCP/IP
                    if (Regex.IsMatch(line, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:\d+\b"))
                    {
                        state = ConnectionState.CONNECTED_WIFI;
                    }
                    else
                    {
                        state = ConnectionState.CONNECTED_USB;
                    }
                    
                    // Prioritize returning immediately so we pick the first one, or just return.
                    return state;
                }
            }

            return foundDevice ? ConnectionState.UNAUTHORIZED : ConnectionState.NO_DEVICE;
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
