using System;
using RMX3171ControlCentre.Models;

namespace RMX3171ControlCentre.Services.Security
{
    public interface IAppModeService
    {
        AppMode CurrentMode { get; }
        event EventHandler ModeChanged;
        void EnableAdvancedMode();
        void DisableAdvancedMode();
        bool IsModificationAllowed(RiskLevel risk);
    }

    public class AppModeService : IAppModeService
    {
        public AppMode CurrentMode { get; private set; } = AppMode.ReadOnly;

        public event EventHandler? ModeChanged;

        public void EnableAdvancedMode()
        {
            if (CurrentMode != AppMode.Advanced)
            {
                CurrentMode = AppMode.Advanced;
                ModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void DisableAdvancedMode()
        {
            if (CurrentMode != AppMode.ReadOnly)
            {
                CurrentMode = AppMode.ReadOnly;
                ModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsModificationAllowed(RiskLevel risk)
        {
            if (risk == RiskLevel.Safe) return true;
            if (CurrentMode == AppMode.Advanced)
            {
                if (risk == RiskLevel.HighRisk) return false; // Hardcode block for now as per requirement
                return true;
            }
            return false;
        }
    }
}
