using System.Windows;
using RMX3171ControlCentre.ViewModels;

namespace RMX3171ControlCentre.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
