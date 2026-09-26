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
        void LogDebug(string message);
        void LogWarning(string message);
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

        private readonly object _fileLock = new object();

        public void LogCommand(string command, string output, string error, int exitCode, bool isReadOnly = true)
        {
            string truncatedOutput = output;
            if (truncatedOutput != null && truncatedOutput.Length > 1000)
            {
                truncatedOutput = truncatedOutput.Substring(0, 1000) + "... [TRUNCATED]";
            }

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Command = command,
                Output = truncatedOutput,
                Error = error,
                ExitCode = exitCode,
                IsReadOnly = isReadOnly
            };

            AddLogEntry(entry);
            WriteToFile($"[{entry.Timestamp:HH:mm:ss}] CMD: {command} | EXIT: {exitCode} | OUT: {truncatedOutput.Replace(Environment.NewLine, " ")} | ERR: {error}");
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
            
            AddLogEntry(entry);
            WriteToFile($"[{entry.Timestamp:HH:mm:ss}] MSG: {message}");
        }

        private void AddLogEntry(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    Logs.Add(entry);
                    while (Logs.Count > 500)
                    {
                        Logs.RemoveAt(0);
                    }
                }));
            }
            else
            {
                Logs.Add(entry);
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(0);
                }
            }
        }

        public void LogDebug(string message)
        {
            WriteToFile($"[{DateTime.Now:HH:mm:ss}] DEBUG:\n{message}");
        }

        public void LogWarning(string message)
        {
            WriteToFile($"[{DateTime.Now:HH:mm:ss}] WARNING: {message}");
        }

        private void WriteToFile(string text)
        {
            Task.Run(() =>
            {
                try
                {
                    lock (_fileLock)
                    {
                        File.AppendAllText(_logFilePath, text + Environment.NewLine);
                    }
                }
                catch { }
            });
        }
    }
}
