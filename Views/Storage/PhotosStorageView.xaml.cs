using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.ViewModels.Storage;

namespace RMX3171ControlCentre.Views.Storage
{
    public partial class PhotosStorageView : UserControl
    {
        private static readonly double[] ZoomSteps = { 0.25, 0.50, 0.75, 1.00, 1.50, 2.00, 3.00, 4.00 };

        private bool _isFitMode = true;
        private double _currentScale = 1.0;
        private bool _isDragging = false;
        private Point _lastDragPoint;

        public PhotosStorageView()
        {
            InitializeComponent();
            DataContextChanged += PhotosStorageView_DataContextChanged;
        }

        private void PhotosStorageView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is PhotosStorageViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.OnFitRequested -= ResetToFit;
                oldVm.OnActualSizeRequested -= ZoomToActualSize;
                oldVm.OnZoomInRequested -= ZoomIn;
                oldVm.OnZoomOutRequested -= ZoomOut;
            }
            if (e.NewValue is PhotosStorageViewModel newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
                newVm.OnFitRequested += ResetToFit;
                newVm.OnActualSizeRequested += ZoomToActualSize;
                newVm.OnZoomInRequested += ZoomIn;
                newVm.OnZoomOutRequested += ZoomOut;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PhotosStorageViewModel.ViewerImageSource))
            {
                Dispatcher.InvokeAsync(ResetToFit, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else if (e.PropertyName == nameof(PhotosStorageViewModel.ViewerVisible))
            {
                if (DataContext is PhotosStorageViewModel vm && vm.ViewerVisible)
                {
                    Dispatcher.InvokeAsync(ResetToFit, System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        // --- VIEWER SIZING, ZOOM, AND PAN LOGIC ---

        private double CalculateFitScale(double imgW, double imgH, double viewportW, double viewportH)
        {
            if (imgW <= 0 || imgH <= 0 || viewportW <= 0 || viewportH <= 0) return 1.0;

            double scaleX = viewportW / imgW;
            double scaleY = viewportH / imgH;
            double fitScale = Math.Min(scaleX, scaleY);

            // Small image: do not unnecessarily upscale beyond 100% natural size
            if (imgW <= viewportW && imgH <= viewportH)
            {
                fitScale = Math.Min(fitScale, 1.0);
            }

            return fitScale;
        }

        public void ResetToFit()
        {
            _isFitMode = true;
            _isDragging = false;
            if (ViewerScrollViewer.IsMouseCaptured) ViewerScrollViewer.ReleaseMouseCapture();

            BitmapSource? bmp = ViewerImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0)
            {
                ViewerImage.Width = double.NaN;
                ViewerImage.Height = double.NaN;
                UpdateCursor();
                return;
            }

            double viewportW = ViewerScrollViewer.ViewportWidth > 0 ? ViewerScrollViewer.ViewportWidth : ViewerScrollViewer.ActualWidth;
            double viewportH = ViewerScrollViewer.ViewportHeight > 0 ? ViewerScrollViewer.ViewportHeight : ViewerScrollViewer.ActualHeight;

            if (viewportW <= 0 || viewportH <= 0) return;

            double naturalW = bmp.PixelWidth;
            double naturalH = bmp.PixelHeight;

            double fitScale = CalculateFitScale(naturalW, naturalH, viewportW, viewportH);
            _currentScale = fitScale;

            double targetW = Math.Round(naturalW * fitScale);
            double targetH = Math.Round(naturalH * fitScale);

            ViewerImage.Width = targetW;
            ViewerImage.Height = targetH;

            ViewerImageContainer.Width = viewportW;
            ViewerImageContainer.Height = viewportH;

            ViewerScrollViewer.UpdateLayout();
            ViewerScrollViewer.ScrollToHorizontalOffset(0);
            ViewerScrollViewer.ScrollToVerticalOffset(0);

            UpdateCursor();
        }

        public void ZoomToActualSize()
        {
            BitmapSource? bmp = ViewerImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

            double viewportW = ViewerScrollViewer.ViewportWidth > 0 ? ViewerScrollViewer.ViewportWidth : ViewerScrollViewer.ActualWidth;
            double viewportH = ViewerScrollViewer.ViewportHeight > 0 ? ViewerScrollViewer.ViewportHeight : ViewerScrollViewer.ActualHeight;
            if (viewportW <= 0 || viewportH <= 0) return;

            _isFitMode = false;
            ApplyZoomCentered(1.0, bmp.PixelWidth, bmp.PixelHeight, viewportW, viewportH);
        }

        public void ZoomIn()
        {
            BitmapSource? bmp = ViewerImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

            double viewportW = ViewerScrollViewer.ViewportWidth > 0 ? ViewerScrollViewer.ViewportWidth : ViewerScrollViewer.ActualWidth;
            double viewportH = ViewerScrollViewer.ViewportHeight > 0 ? ViewerScrollViewer.ViewportHeight : ViewerScrollViewer.ActualHeight;
            if (viewportW <= 0 || viewportH <= 0) return;

            double fitScale = CalculateFitScale(bmp.PixelWidth, bmp.PixelHeight, viewportW, viewportH);
            double activeScale = _isFitMode ? fitScale : _currentScale;

            double nextScale = ZoomSteps.FirstOrDefault(s => s > activeScale * 1.05);
            if (nextScale == 0) nextScale = 4.0;

            _isFitMode = false;
            ApplyZoomCentered(nextScale, bmp.PixelWidth, bmp.PixelHeight, viewportW, viewportH);
        }

        public void ZoomOut()
        {
            BitmapSource? bmp = ViewerImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

            double viewportW = ViewerScrollViewer.ViewportWidth > 0 ? ViewerScrollViewer.ViewportWidth : ViewerScrollViewer.ActualWidth;
            double viewportH = ViewerScrollViewer.ViewportHeight > 0 ? ViewerScrollViewer.ViewportHeight : ViewerScrollViewer.ActualHeight;
            if (viewportW <= 0 || viewportH <= 0) return;

            double fitScale = CalculateFitScale(bmp.PixelWidth, bmp.PixelHeight, viewportW, viewportH);
            double activeScale = _isFitMode ? fitScale : _currentScale;

            double prevScale = ZoomSteps.LastOrDefault(s => s < activeScale * 0.95);
            if (prevScale == 0 || prevScale <= fitScale * 1.02)
            {
                ResetToFit();
                return;
            }

            _isFitMode = false;
            ApplyZoomCentered(prevScale, bmp.PixelWidth, bmp.PixelHeight, viewportW, viewportH);
        }

        private void ApplyZoomCentered(double scale, double naturalW, double naturalH, double viewportW, double viewportH)
        {
            _currentScale = scale;
            double targetW = Math.Round(naturalW * scale);
            double targetH = Math.Round(naturalH * scale);

            ViewerImage.Width = targetW;
            ViewerImage.Height = targetH;
            ViewerImageContainer.Width = Math.Max(viewportW, targetW);
            ViewerImageContainer.Height = Math.Max(viewportH, targetH);

            ViewerScrollViewer.UpdateLayout();

            double scrollX = Math.Max(0, (targetW - viewportW) / 2.0);
            double scrollY = Math.Max(0, (targetH - viewportH) / 2.0);

            ViewerScrollViewer.ScrollToHorizontalOffset(scrollX);
            ViewerScrollViewer.ScrollToVerticalOffset(scrollY);

            UpdateCursor();
        }

        private void ApplyZoomAndCenterOnPoint(double scale, double naturalW, double naturalH, double viewportW, double viewportH, double relX, double relY, Point mousePos)
        {
            _currentScale = scale;
            double targetW = Math.Round(naturalW * scale);
            double targetH = Math.Round(naturalH * scale);

            ViewerImage.Width = targetW;
            ViewerImage.Height = targetH;
            ViewerImageContainer.Width = Math.Max(viewportW, targetW);
            ViewerImageContainer.Height = Math.Max(viewportH, targetH);

            ViewerScrollViewer.UpdateLayout();

            double newImgOffsetX = (ViewerImageContainer.Width - targetW) / 2.0;
            double newImgOffsetY = (ViewerImageContainer.Height - targetH) / 2.0;

            double newContentX = newImgOffsetX + (targetW * relX);
            double newContentY = newImgOffsetY + (targetH * relY);

            double targetScrollX = newContentX - mousePos.X;
            double targetScrollY = newContentY - mousePos.Y;

            ViewerScrollViewer.ScrollToHorizontalOffset(targetScrollX);
            ViewerScrollViewer.ScrollToVerticalOffset(targetScrollY);

            UpdateCursor();
        }

        private void ViewerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (DataContext is not PhotosStorageViewModel vm || !vm.ViewerVisible) return;

            BitmapSource? bmp = ViewerImage.Source as BitmapSource;
            if (bmp == null || bmp.PixelWidth <= 0 || bmp.PixelHeight <= 0) return;

            double naturalW = bmp.PixelWidth;
            double naturalH = bmp.PixelHeight;
            double viewportW = ViewerScrollViewer.ViewportWidth > 0 ? ViewerScrollViewer.ViewportWidth : ViewerScrollViewer.ActualWidth;
            double viewportH = ViewerScrollViewer.ViewportHeight > 0 ? ViewerScrollViewer.ViewportHeight : ViewerScrollViewer.ActualHeight;
            if (viewportW <= 0 || viewportH <= 0) return;

            double fitScale = CalculateFitScale(naturalW, naturalH, viewportW, viewportH);

            Point mousePos = e.GetPosition(ViewerScrollViewer);

            double oldContentX = ViewerScrollViewer.HorizontalOffset + mousePos.X;
            double oldContentY = ViewerScrollViewer.VerticalOffset + mousePos.Y;

            double oldImageW = ViewerImage.Width;
            double oldImageH = ViewerImage.Height;
            double containerW = ViewerImageContainer.Width;
            double containerH = ViewerImageContainer.Height;

            double imgOffsetX = (containerW - oldImageW) / 2.0;
            double imgOffsetY = (containerH - oldImageH) / 2.0;

            double relX = oldImageW > 0 ? (oldContentX - imgOffsetX) / oldImageW : 0.5;
            double relY = oldImageH > 0 ? (oldContentY - imgOffsetY) / oldImageH : 0.5;
            relX = Math.Clamp(relX, 0.0, 1.0);
            relY = Math.Clamp(relY, 0.0, 1.0);

            double oldScale = _isFitMode ? fitScale : _currentScale;
            double newScale;

            if (e.Delta > 0)
            {
                newScale = oldScale * 1.25;
                if (newScale > 4.0) newScale = 4.0;
            }
            else
            {
                newScale = oldScale / 1.25;
                if (newScale <= fitScale * 1.02)
                {
                    ResetToFit();
                    e.Handled = true;
                    return;
                }
                if (newScale < 0.25) newScale = 0.25;
            }

            _isFitMode = false;
            ApplyZoomAndCenterOnPoint(newScale, naturalW, naturalH, viewportW, viewportH, relX, relY, mousePos);
            e.Handled = true;
        }

        private void ViewerScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not PhotosStorageViewModel vm || !vm.ViewerVisible) return;

            // Double click inside viewer must not trigger zoom or close
            if (e.ClickCount == 2)
            {
                e.Handled = true;
                return;
            }

            if (ViewerScrollViewer.ScrollableWidth > 0 || ViewerScrollViewer.ScrollableHeight > 0)
            {
                _isDragging = true;
                _lastDragPoint = e.GetPosition(ViewerScrollViewer);
                ViewerScrollViewer.CaptureMouse();
                ViewerScrollViewer.Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void ViewerScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(ViewerScrollViewer);
                Vector delta = _lastDragPoint - currentPoint;
                _lastDragPoint = currentPoint;

                ViewerScrollViewer.ScrollToHorizontalOffset(ViewerScrollViewer.HorizontalOffset + delta.X);
                ViewerScrollViewer.ScrollToVerticalOffset(ViewerScrollViewer.VerticalOffset + delta.Y);
                e.Handled = true;
            }
            else
            {
                UpdateCursor();
            }
        }

        private void ViewerScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ViewerScrollViewer.ReleaseMouseCapture();
                UpdateCursor();
                e.Handled = true;
            }
        }

        private void ViewerScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is not PhotosStorageViewModel vm || !vm.ViewerVisible) return;

            if (_isFitMode)
            {
                ResetToFit();
            }
            else
            {
                BitmapSource? bmp = ViewerImage.Source as BitmapSource;
                if (bmp != null && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                {
                    double viewportW = e.NewSize.Width;
                    double viewportH = e.NewSize.Height;
                    ViewerImageContainer.Width = Math.Max(viewportW, ViewerImage.Width);
                    ViewerImageContainer.Height = Math.Max(viewportH, ViewerImage.Height);
                    UpdateCursor();
                }
            }
        }

        private void UpdateCursor()
        {
            if (_isDragging)
            {
                ViewerScrollViewer.Cursor = Cursors.SizeAll;
            }
            else if (ViewerScrollViewer.ScrollableWidth > 0 || ViewerScrollViewer.ScrollableHeight > 0)
            {
                ViewerScrollViewer.Cursor = Cursors.Hand;
            }
            else
            {
                ViewerScrollViewer.Cursor = Cursors.Arrow;
            }
        }

        private void FitButton_Click(object sender, RoutedEventArgs e) => ResetToFit();
        private void ActualSizeButton_Click(object sender, RoutedEventArgs e) => ZoomToActualSize();
        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomIn();
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOut();

        // --- GRID ROW GROUPING (VIRTUALIZATION) ---

        private void GridContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is PhotosStorageViewModel vm)
            {
                double width = e.NewSize.Width;
                int cols = Math.Max(1, (int)(width / 190)); // 180 + 10 margin
                vm.UpdateGridColumns(cols);
            }
        }

        // --- PHOTO CARD INTERACTION ---

        private void PhotoCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PhotoItem photo)
            {
                if (DataContext is PhotosStorageViewModel vm)
                {
                    vm.ThumbnailLoader.RequestThumbnail(photo);
                }
            }
        }

        private void PhotoCard_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PhotoItem photo)
            {
                if (DataContext is PhotosStorageViewModel vm)
                {
                    vm.ThumbnailLoader.CancelRequest(photo);
                }
            }
        }

        private void PhotoCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PhotoItem photo)
            {
                if (DataContext is PhotosStorageViewModel vm)
                {
                    if (e.ClickCount == 2)
                    {
                        vm.HandlePhotoDoubleClick(photo);
                        e.Handled = true;
                        return;
                    }

                    vm.HandlePhotoClick(photo,
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    e.Handled = true; // Prevent ListBoxItem from intercepting
                }
            }
        }

        private void PhotoCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Prevent bubble
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is PhotoItem photo)
            {
                if (DataContext is PhotosStorageViewModel vm)
                {
                    vm.HandlePhotoDoubleClick(photo);
                    e.Handled = true;
                }
            }
        }

        private void PhotoDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is PhotosStorageViewModel vm && sender is DataGrid dg)
            {
                vm.UpdateSelection(dg.SelectedItems.Cast<PhotoItem>());
            }
        }

        // --- VIEWER SHORTCUTS (KEYBOARD) ---

        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is PhotosStorageViewModel vm && vm.ViewerVisible)
            {
                if (e.Key == Key.Escape)
                {
                    vm.CloseViewerCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    vm.PrevViewerPhotoCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    vm.NextViewerPhotoCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
