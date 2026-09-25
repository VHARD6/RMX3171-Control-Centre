using System;
using System.Collections.ObjectModel;
using System.IO;

namespace RMX3171ControlCentre.Services.Adb
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Command { get; set; } = "";
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public int ExitCode { get; set; }
        public bool IsReadOnly { get; set; } = true;
    }

    public interface ILogService
    {
        ObservableCollection<LogEntry> Logs { get; }
        void LogCommand(string command, string output, string error, int exitCode, bool isReadOnly = true);
        void LogMessage(string message);
    }

    public class LogService : ILogService
    {
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();
        private string _logFilePath;

        public LogService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(appData, "RMX3171ControlCentre", "Logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"app_log_{DateTime.Now:yyyyMMdd}.txt");
        }

        public void LogCommand(string command, string output, string error, int exitCode, bool isReadOnly = true)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Command = command,
                Output = output,
                Error = error,
                ExitCode = exitCode,
                IsReadOnly = isReadOnly
            };

            // Update UI on dispatcher if needed, but since it's an observable collection bound to UI, 
            // WPF CommunityToolkit typically handles this if done correctly, or we invoke.
            System.Windows.Application.Current.Dispatcher.Invoke(() => Logs.Add(entry));
            
            WriteToFile($"[{entry.Timestamp:HH:mm:ss}] CMD: {command} | EXIT: {exitCode} | OUT: {output} | ERR: {error}");
        }

        public void LogMessage(string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Command = "APP_INFO",
                Output = message,
                IsReadOnly = true
            };
            
            System.Windows.Application.Current.Dispatcher.Invoke(() => Logs.Add(entry));
            WriteToFile($"[{entry.Timestamp:HH:mm:ss}] MSG: {message}");
        }

        private void WriteToFile(string text)
        {
            try
            {
                File.AppendAllText(_logFilePath, text + Environment.NewLine);
            }
            catch { }
        }
    }
}
