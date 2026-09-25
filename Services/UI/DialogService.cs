using System.Threading.Tasks;
using System.Windows;
using RMX3171ControlCentre.Views.Dialogs;

namespace RMX3171ControlCentre.Services.UI
{
    public interface IDialogService
    {
        Task<bool> ShowModificationPreviewAsync(string operation, string target, string currentState, string requestedChange, string adbCommand, string risk, string consequences);
        void ShowMessage(string title, string message);
    }

    public class DialogService : IDialogService
    {
        public Task<bool> ShowModificationPreviewAsync(string operation, string target, string currentState, string requestedChange, string adbCommand, string risk, string consequences)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dialog = new ModificationPreviewWindow(operation, target, currentState, requestedChange, adbCommand, risk, consequences)
                {
                    Owner = Application.Current.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                bool? result = dialog.ShowDialog();
                tcs.SetResult(result == true);
            });

            return tcs.Task;
        }

        public void ShowMessage(string title, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
    }
}
