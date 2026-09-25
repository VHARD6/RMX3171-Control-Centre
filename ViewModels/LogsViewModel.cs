using CommunityToolkit.Mvvm.ComponentModel;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.ViewModels
{
    public partial class LogsViewModel : ViewModelBase
    {
        public ILogService LogService { get; }

        public LogsViewModel(ILogService logService)
        {
            LogService = logService;
        }
    }
}
