using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RMX3171ControlCentre.Models;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.Services.Device
{
    public interface IAppManagerService
    {
        Task<List<AppPackageInfo>> GetInstalledPackagesAsync();
        Task<bool> DisableAppAsync(string packageName);
        Task<bool> EnableAppAsync(string packageName);
        Task<bool> UninstallAppForUserAsync(string packageName);
    }

    public class AppManagerService : IAppManagerService
    {
        private readonly IAdbService _adbService;
        private readonly IMemoryService _memoryService; // for checking protected list and RAM

        public AppManagerService(IAdbService adbService, IMemoryService memoryService)
        {
            _adbService = adbService;
            _memoryService = memoryService;
        }

        public async Task<List<AppPackageInfo>> GetInstalledPackagesAsync()
        {
            var apps = new List<AppPackageInfo>();

            // Get all packages
            var allResult = await _adbService.ExecuteCommandAsync("shell pm list packages -f");
            if (allResult.ExitCode != 0) return apps;

            var sysResult = await _adbService.ExecuteCommandAsync("shell pm list packages -s");
            var sysPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sysResult.ExitCode == 0)
            {
                var lines = sysResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("package:")) sysPackages.Add(line.Substring(8));
                }
            }

            var disabledResult = await _adbService.ExecuteCommandAsync("shell pm list packages -d");
            var disabledPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (disabledResult.ExitCode == 0)
            {
                var lines = disabledResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("package:")) disabledPackages.Add(line.Substring(8));
                }
            }

            // Optional: get memory usage from memory service
            var consumers = await _memoryService.GetTopConsumersAsync();

            var allLines = allResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in allLines)
            {
                if (!line.StartsWith("package:")) continue;

                // Format is usually package:/path/to/apk=com.package.name
                int eqIndex = line.LastIndexOf('=');
                if (eqIndex > -1)
                {
                    string pkg = line.Substring(eqIndex + 1);
                    
                    bool isSys = sysPackages.Contains(pkg);
                    bool isProtected = isSys && IsProtectedCore(pkg); // Mark core system as protected

                    var memInfo = consumers.FirstOrDefault(c => c.PackageName == pkg);

                    apps.Add(new AppPackageInfo
                    {
                        PackageName = pkg,
                        AppName = pkg,
                        IsSystem = isSys,
                        IsProtected = isProtected,
                        IsEnabled = !disabledPackages.Contains(pkg),
                        RamMb = memInfo?.RamMb ?? 0
                    });
                }
            }

            return apps.OrderBy(a => a.PackageName).ToList();
        }

        private bool IsProtectedCore(string packageName)
        {
            var protectedStarts = new[] { "com.android.", "com.coloros.", "com.oplus.", "com.realme.", "android" };
            foreach (var start in protectedStarts)
            {
                if (packageName.StartsWith(start, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (packageName.Contains("google")) return true; // generic protection for google core apps
            return false;
        }

        public async Task<bool> DisableAppAsync(string packageName)
        {
            var result = await _adbService.ExecuteCommandAsync($"shell pm disable-user --user 0 {packageName}", isReadOnly: false);
            return result.ExitCode == 0;
        }

        public async Task<bool> EnableAppAsync(string packageName)
        {
            var result = await _adbService.ExecuteCommandAsync($"shell pm enable {packageName}", isReadOnly: false);
            return result.ExitCode == 0;
        }

        public async Task<bool> UninstallAppForUserAsync(string packageName)
        {
            var result = await _adbService.ExecuteCommandAsync($"shell pm uninstall -k --user 0 {packageName}", isReadOnly: false);
            return result.ExitCode == 0;
        }
    }
}
