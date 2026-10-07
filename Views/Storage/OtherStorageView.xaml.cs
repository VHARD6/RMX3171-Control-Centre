using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.ViewModels.Storage;

namespace RMX3171ControlCentre.Views.Storage
{
    public partial class OtherStorageView : UserControl
    {
        private const double DesiredItemWidth = 200.0;
        private const double DesiredItemHeight = 200.0;

        public OtherStorageView()
        {
            InitializeComponent();
            
            DataContextChanged += OnDataContextChanged;
            Loaded += OtherStorageView_Loaded;
        }

        private void OtherStorageView_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.InvokeAsync(RecalculateColumnsFromActualWidth, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is OtherStorageViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_LayoutPropertyChanged;
            }
            
            if (e.NewValue is OtherStorageViewModel newVm)
            {
                newVm.PropertyChanged += Vm_LayoutPropertyChanged;
            }
        }

        private void Vm_LayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OtherStorageViewModel.IsFolderView))
            {
                if (ViewModel != null && !ViewModel.IsFolderView)
                {
                    Dispatcher.InvokeAsync(RecalculateColumnsFromActualWidth,
                        System.Windows.Threading.DispatcherPriority.Render);
                }
            }
        }

        private void RecalculateColumnsFromActualWidth()
        {
            if (ViewModel == null || ViewModel.IsFolderView || !ViewModel.IsGridView) return;

            double width = OthersVirtualListBox.ActualWidth;
            if (width <= 0) width = this.ActualWidth;
            if (width <= 0) return;

            double availableWidth = width - 20; // scrollbar padding
            if (availableWidth < DesiredItemWidth) availableWidth = DesiredItemWidth;
            int columns = (int)Math.Floor(availableWidth / DesiredItemWidth);
            if (columns < 1) columns = 1;
            ViewModel.UpdateGridColumns(columns);
        }

        private OtherStorageViewModel? ViewModel => DataContext as OtherStorageViewModel;

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;

            if (e.Key == Key.Escape && ViewModel.ViewerVisible)
            {
                if (ViewModel.CloseViewerCommand.CanExecute(null))
                {
                    ViewModel.CloseViewerCommand.Execute(null);
                    e.Handled = true;
                }
                return;
            }

            if (ViewModel.IsFolderView || !ViewModel.IsGridView) return;

            if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var all = ViewModel.FolderOthersView.Cast<OtherItem>().ToList();
                ViewModel.UpdateSelection(all);
                e.Handled = true;
            }
        }

        private void OthersVirtualListBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsGridView || ViewModel.IsFolderView) return;

            double availableWidth = e.NewSize.Width - 20; 
            if (availableWidth < DesiredItemWidth) availableWidth = DesiredItemWidth;

            int columns = (int)Math.Floor(availableWidth / DesiredItemWidth);
            if (columns < 1) columns = 1;

            ViewModel.UpdateGridColumns(columns);
        }

        private void OtherCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is OtherItem item)
            {
                if (e.ClickCount == 2)
                {
                    ViewModel?.HandleOtherDoubleClick(item);
                    e.Handled = true;
                    return;
                }

                bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                
                ViewModel?.HandleOtherClick(item, isCtrl, isShift);
                
                if (!OthersVirtualListBox.IsFocused)
                {
                    OthersVirtualListBox.Focus();
                }
                
                e.Handled = true;
            }
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is OtherItem item)
            {
                ViewModel?.HandleOtherDoubleClick(item);
                e.Handled = true;
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && ViewModel != null)
            {
                if (grid.SelectedItems.Count > 0)
                {
                    ViewModel.UpdateSelection(grid.SelectedItems.Cast<OtherItem>());
                }
                else
                {
                    ViewModel.UpdateSelection(Array.Empty<OtherItem>());
                }
            }
        }
    }
}


