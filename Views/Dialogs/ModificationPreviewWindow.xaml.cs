using System.Windows;

namespace RMX3171ControlCentre.Views.Dialogs
{
    public partial class ModificationPreviewWindow : Window
    {
        public ModificationPreviewWindow(string operation, string target, string currentState, string requestedChange, string adbCommand, string risk, string consequences)
        {
            InitializeComponent();
            
            OpText.Text = operation;
            TargetText.Text = target;
            StateText.Text = $"{currentState} -> {requestedChange}";
            RiskText.Text = risk;
            ConsequencesText.Text = consequences;
            CommandText.Text = adbCommand;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
