using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RMX3171ControlCentre.Services.Device
{
    public interface ICleanerConfigService
    {
        Task<HashSet<string>> GetExcludedPackagesAsync();
        Task SaveExcludedPackagesAsync(HashSet<string> packages);
    }

    public class CleanerConfigService : ICleanerConfigService
    {
        private readonly string _configFilePath;

        public CleanerConfigService()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RMX3171ControlCentre");
            Directory.CreateDirectory(appDataDir);
            _configFilePath = Path.Combine(appDataDir, "cleaner_exclusions.json");
        }

        public async Task<HashSet<string>> GetExcludedPackagesAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = await File.ReadAllTextAsync(_configFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors, return empty set
            }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task SaveExcludedPackagesAsync(HashSet<string> packages)
        {
            try
            {
                string json = JsonSerializer.Serialize(new List<string>(packages));
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch
            {
                // Ignore saving errors for now
            }
        }
    }
}
