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
        CONNECTED
    }

    public interface IDeviceService
    {
        Task<ConnectionState> GetConnectionStateAsync();
        Task<Dictionary<string, string>> GetDevicePropertiesAsync();
        Task<string> GetStorageInfoAsync();
        Task<string> GetBatteryInfoAsync();
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
            var result = await _adbService.ExecuteCommandAsync("devices");
            if (result.ExitCode != 0) return ConnectionState.NO_DEVICE;

            var lines = result.Output.Split('\n');
            bool foundDevice = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices attached")) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;

                foundDevice = true;
                if (line.Contains("unauthorized")) return ConnectionState.UNAUTHORIZED;
                if (line.Contains("offline")) return ConnectionState.OFFLINE;
                if (line.Contains("device")) return ConnectionState.CONNECTED;
            }

            return foundDevice ? ConnectionState.CONNECTED : ConnectionState.NO_DEVICE;
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
