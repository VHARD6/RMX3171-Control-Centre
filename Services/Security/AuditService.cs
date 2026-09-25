using System;
using System.IO;
using RMX3171ControlCentre.Models;

namespace RMX3171ControlCentre.Services.Security
{
    public interface IAuditService
    {
        void LogModification(string operation, string target, string previousValue, string newValue, string adbCommand, bool success);
    }

    public class AuditService : IAuditService
    {
        private readonly string _logFilePath;

        public AuditService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "RMX3171ControlCentre");
            Directory.CreateDirectory(folder);
            _logFilePath = Path.Combine(folder, "audit_log.txt");
        }

        public void LogModification(string operation, string target, string previousValue, string newValue, string adbCommand, bool success)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string status = success ? "SUCCESS" : "FAILURE";
            
            string logEntry = $"[{timestamp}] {status} | Op: {operation} | Target: {target} | Old: {previousValue} | New: {newValue} | Cmd: {adbCommand}\n";
            
            File.AppendAllText(_logFilePath, logEntry);
        }
    }
}
