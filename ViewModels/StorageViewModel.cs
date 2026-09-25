using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMX3171ControlCentre.Services.Device;
using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace RMX3171ControlCentre.ViewModels
{
    public class PartitionInfo
    {
        public string Filesystem { get; set; } = "";
        public string Size { get; set; } = "";
        public string Used { get; set; } = "";
        public string Free { get; set; } = "";
        public string Usage { get; set; } = "";
        public string MountedOn { get; set; } = "";
    }

    public partial class StorageViewModel : ViewModelBase
    {
        private readonly IDeviceService _deviceService;

        [ObservableProperty] private string _storageRawInfo = "Click Refresh to load storage info.";
        
        [ObservableProperty] private string _internalTotal = "0 GB";
        [ObservableProperty] private string _internalUsed = "0 GB";
        [ObservableProperty] private string _internalFree = "0 GB";
        [ObservableProperty] private int _internalUsagePercent = 0;
        [ObservableProperty] private string _internalUsageText = "0%";

        [ObservableProperty] private bool _isAdvancedVisible = false;

        public ObservableCollection<PartitionInfo> Partitions { get; } = new ObservableCollection<PartitionInfo>();

        public StorageViewModel(IDeviceService deviceService)
        {
            _deviceService = deviceService;
        }

        [RelayCommand]
        private void ToggleAdvanced()
        {
            IsAdvancedVisible = !IsAdvancedVisible;
        }

        [RelayCommand]
        private async Task RefreshStorageAsync()
        {
            StorageRawInfo = "Loading...";
            var raw = await _deviceService.GetStorageInfoAsync();
            StorageRawInfo = raw;
            ParseStorageInfo(raw);
        }

        private void ParseStorageInfo(string rawOutput)
        {
            Partitions.Clear();
            var lines = rawOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            
            // Regex to match typical df -h output on Android
            // Filesystem Size Used Avail Use% Mounted on
            var regex = new Regex(@"^(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(.+)$");

            foreach (var line in lines)
            {
                if (line.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase)) continue;

                var match = regex.Match(line);
                if (match.Success)
                {
                    var partition = new PartitionInfo
                    {
                        Filesystem = match.Groups[1].Value,
                        Size = FormatUnit(match.Groups[2].Value),
                        Used = FormatUnit(match.Groups[3].Value),
                        Free = FormatUnit(match.Groups[4].Value),
                        Usage = match.Groups[5].Value,
                        MountedOn = match.Groups[6].Value
                    };
                    Partitions.Add(partition);

                    // /data represents user internal storage
                    if (partition.MountedOn == "/data")
                    {
                        InternalTotal = partition.Size;
                        InternalUsed = partition.Used;
                        InternalFree = partition.Free;
                        InternalUsageText = partition.Usage;
                        
                        if (int.TryParse(partition.Usage.Replace("%", ""), out int percent))
                        {
                            InternalUsagePercent = percent;
                        }
                    }
                }
            }
        }

        private string FormatUnit(string raw)
        {
            if (raw.EndsWith("G", StringComparison.OrdinalIgnoreCase)) return raw.Replace("G", " GB", StringComparison.OrdinalIgnoreCase);
            if (raw.EndsWith("M", StringComparison.OrdinalIgnoreCase)) return raw.Replace("M", " MB", StringComparison.OrdinalIgnoreCase);
            if (raw.EndsWith("K", StringComparison.OrdinalIgnoreCase)) return raw.Replace("K", " KB", StringComparison.OrdinalIgnoreCase);
            if (raw.EndsWith("B", StringComparison.OrdinalIgnoreCase)) return raw.Replace("B", " B", StringComparison.OrdinalIgnoreCase);
            return raw;
        }
    }
}
