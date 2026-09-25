using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RMX3171ControlCentre.Services.Security;

namespace RMX3171ControlCentre.Services.Adb
{
    public interface IAdbService
    {
        Task<(string Output, string Error, int ExitCode)> ExecuteCommandAsync(string arguments, bool isReadOnly = true, CancellationToken cancellationToken = default);
        Task<bool> IsAdbAvailableAsync();
        Task RestartAdbServerAsync();
    }

    public class AdbService : IAdbService
    {
        private readonly ILogService _logService;
        private readonly Security.IAppModeService _appModeService;
        private string _adbPath = "adb"; // default to system path

        public AdbService(ILogService logService, Security.IAppModeService appModeService)
        {
            _logService = logService;
            _appModeService = appModeService;
            CheckBundledAdb();
        }

        private void CheckBundledAdb()
        {
            var bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe");
            if (File.Exists(bundledPath))
            {
                _adbPath = bundledPath;
                _logService.LogMessage($"Using bundled adb at {_adbPath}");
            }
            else
            {
                _logService.LogMessage("Bundled adb not found, relying on system PATH.");
            }
        }

        public async Task<bool> IsAdbAvailableAsync()
        {
            var result = await ExecuteCommandAsync("version");
            return result.ExitCode == 0 && result.Output.Contains("Android Debug Bridge");
        }

        public async Task RestartAdbServerAsync()
        {
            await ExecuteCommandAsync("kill-server");
            await ExecuteCommandAsync("start-server");
        }

        public async Task<(string Output, string Error, int ExitCode)> ExecuteCommandAsync(string arguments, bool isReadOnly = true, CancellationToken cancellationToken = default)
        {
            if (!isReadOnly && _appModeService.CurrentMode != Models.AppMode.Advanced)
            {
                _logService.LogMessage($"BLOCKED: Attempted to run non-read-only command in ReadOnly mode: adb {arguments}");
                return ("", "Command blocked by safety policy. Advanced Mode required.", -1);
            }
            if (!isReadOnly)
            {
                _logService.LogMessage($"WARNING: Running non-read-only command in Advanced Mode: adb {arguments}");
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            string output = "";
            string error = "";
            int exitCode = -1;

            try
            {
                using var process = new Process { StartInfo = processStartInfo };
                
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAny(Task.WhenAll(outputTask, errorTask), Task.Delay(10000, cancellationToken));
                
                if (!process.HasExited)
                {
                    process.Kill();
                    error = "Command timed out.";
                }
                else
                {
                    output = await outputTask;
                    error = await errorTask;
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            _logService.LogCommand($"adb {arguments}", output.Trim(), error.Trim(), exitCode, isReadOnly);

            return (output, error, exitCode);
        }
    }
}
