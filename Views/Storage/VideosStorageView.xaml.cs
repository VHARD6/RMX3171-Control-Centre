using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.ViewModels.Storage;

namespace RMX3171ControlCentre.Views.Storage
{
    public partial class VideosStorageView : UserControl
    {
        private const double DesiredItemWidth = 200.0;
        private const double DesiredItemHeight = 200.0;
        private System.Windows.Threading.DispatcherTimer _positionTimer;
        private bool _isDraggingSeeker = false;

        public VideosStorageView()
        {
            InitializeComponent();
            
            _positionTimer = new System.Windows.Threading.DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            DataContextChanged += OnDataContextChanged;
            Loaded += VideosStorageView_Loaded;
        }

        private void VideosStorageView_Loaded(object sender, RoutedEventArgs e)
        {
            // When the UserControl first loads, trigger a column recalculation
            // in case the ViewModel is already in a non-folder state (e.g. re-navigation)
            Dispatcher.InvokeAsync(RecalculateColumnsFromActualWidth, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is VideosStorageViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.PropertyChanged -= Vm_LayoutPropertyChanged;
            }
            
            if (e.NewValue is VideosStorageViewModel newVm)
            {
                newVm.PropertyChanged += Vm_PropertyChanged;
                newVm.PropertyChanged += Vm_LayoutPropertyChanged;
            }
        }

        // Detect when the folder-videos grid becomes visible (IsFolderView -> false)
        // and recalculate columns with the actual rendered width at Render priority
        private void Vm_LayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideosStorageViewModel.IsFolderView))
            {
                if (ViewModel != null && !ViewModel.IsFolderView)
                {
                    // Grid just became Visible; schedule re-measure after layout pass
                    Dispatcher.InvokeAsync(RecalculateColumnsFromActualWidth,
                        System.Windows.Threading.DispatcherPriority.Render);
                }
            }
        }

        private void RecalculateColumnsFromActualWidth()
        {
            if (ViewModel == null || ViewModel.IsFolderView || !ViewModel.IsGridView) return;

            // Use the ListBox's actual rendered width, falling back to the UserControl width
            double width = VideosVirtualListBox.ActualWidth;
            if (width <= 0) width = this.ActualWidth;
            if (width <= 0) return;

            double availableWidth = width - 20; // scrollbar padding
            if (availableWidth < DesiredItemWidth) availableWidth = DesiredItemWidth;
            int columns = (int)Math.Floor(availableWidth / DesiredItemWidth);
            if (columns < 1) columns = 1;
            ViewModel.UpdateGridColumns(columns);
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideosStorageViewModel.ViewerLocalPath))
            {
                if (ViewModel?.ViewerLocalPath != null)
                {
                    // Detect rotation metadata and apply LayoutTransform before playback
                    double rotation = GetVideoRotationDegrees(ViewModel.ViewerLocalPath);
                    VideoPlayer.LayoutTransform = rotation != 0
                        ? new RotateTransform(rotation)
                        : Transform.Identity;

                    VideoPlayer.Source = new Uri(ViewModel.ViewerLocalPath);
                    VideoPlayer.Volume = VolumeSlider.Value;
                    VideoPlayer.Play();
                    _positionTimer.Start();
                    PlayPauseButton.Content = "⏸";
                }
                else
                {
                    _positionTimer.Stop();
                    VideoPlayer.Stop();
                    VideoPlayer.Close();
                    VideoPlayer.Source = null;
                    VideoPlayer.LayoutTransform = Transform.Identity; // Reset rotation
                }
            }
        }

        // ─── Rotation detection via lightweight MP4 binary parsing ──────────────
        private static double GetVideoRotationDegrees(string filePath)
        {
            try
            {
                using (var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    long length = fs.Length;
                    while (fs.Position < length)
                    {
                        long startPos = fs.Position;
                        if (length - startPos < 8) break;

                        byte[] header = new byte[8];
                        int read = fs.Read(header, 0, 8);
                        if (read < 8) break;

                        uint size = (uint)((header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
                        string type = System.Text.Encoding.ASCII.GetString(header, 4, 4);

                        long boxSize = size;
                        long dataOffset = 8;

                        if (size == 1)
                        {
                            if (length - fs.Position < 8) break;
                            byte[] extSize = new byte[8];
                            fs.Read(extSize, 0, 8);
                            boxSize = ((long)extSize[0] << 56) | ((long)extSize[1] << 48) | ((long)extSize[2] << 40) | ((long)extSize[3] << 32) |
                                      ((long)extSize[4] << 24) | ((long)extSize[5] << 16) | ((long)extSize[6] << 8) | extSize[7];
                            dataOffset = 16;
                        }
                        else if (size == 0)
                        {
                            boxSize = length - startPos;
                        }

                        if (type == "moov" || type == "trak")
                        {
                            continue; // Step into container boxes
                        }
                        
                        if (type == "tkhd")
                        {
                            byte[] tkhdData = new byte[boxSize - dataOffset];
                            fs.Read(tkhdData, 0, (int)(boxSize - dataOffset));
                            
                            byte version = tkhdData[0];
                            int offset = (version == 1) ? 32 : 20; 
                            offset += 16; // Skip reserved, layer, volume, etc.
                            
                            if (offset + 36 <= tkhdData.Length)
                            {
                                int a = (tkhdData[offset] << 24) | (tkhdData[offset+1] << 16) | (tkhdData[offset+2] << 8) | tkhdData[offset+3];
                                int b = (tkhdData[offset+4] << 24) | (tkhdData[offset+5] << 16) | (tkhdData[offset+6] << 8) | tkhdData[offset+7];
                                int c = (tkhdData[offset+12] << 24) | (tkhdData[offset+13] << 16) | (tkhdData[offset+14] << 8) | tkhdData[offset+15];
                                int d = (tkhdData[offset+16] << 24) | (tkhdData[offset+17] << 16) | (tkhdData[offset+18] << 8) | tkhdData[offset+19];

                                if (a == 0 && b == 65536 && c == -65536 && d == 0) return 90;
                                if (a == -65536 && b == 0 && c == 0 && d == -65536) return 180;
                                if (a == 0 && b == -65536 && c == 65536 && d == 0) return 270;
                            }
                        }
                        
                        fs.Seek(startPos + boxSize, System.IO.SeekOrigin.Begin);
                    }
                }
            }
            catch { }
            return 0;
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (VideoPlayer.Source != null && VideoPlayer.NaturalDuration.HasTimeSpan && !_isDraggingSeeker)
            {
                SeekSlider.Value = VideoPlayer.Position.TotalSeconds;
                CurrentTimeText.Text = VideoPlayer.Position.ToString(@"mm\:ss");
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = VideoPlayer.NaturalDuration.TimeSpan;
                SeekSlider.Maximum = duration.TotalSeconds;
                TotalTimeText.Text = duration.ToString(@"mm\:ss");
            }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Stop();
            PlayPauseButton.Content = "▶";
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.Source == null) return;
            
            if (PlayPauseButton.Content.ToString() == "⏸")
            {
                VideoPlayer.Pause();
                PlayPauseButton.Content = "▶";
            }
            else
            {
                VideoPlayer.Play();
                PlayPauseButton.Content = "⏸";
            }
        }

        private void SeekSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDraggingSeeker = true;
        }

        private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDraggingSeeker = false;
            if (VideoPlayer.Source != null)
            {
                VideoPlayer.Position = TimeSpan.FromSeconds(SeekSlider.Value);
            }
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSeeker)
            {
                CurrentTimeText.Text = TimeSpan.FromSeconds(SeekSlider.Value).ToString(@"mm\:ss");
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VideoPlayer != null)
            {
                VideoPlayer.Volume = VolumeSlider.Value;
            }
        }

        private VideosStorageViewModel? ViewModel => DataContext as VideosStorageViewModel;

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
                var all = ViewModel.FolderVideosView.Cast<VideoItem>().ToList();
                ViewModel.UpdateSelection(all);
                e.Handled = true;
            }
        }

        private void VideosVirtualListBox_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsGridView || ViewModel.IsFolderView) return;

            double availableWidth = e.NewSize.Width - 20; // Scrollbar padding
            if (availableWidth < DesiredItemWidth) availableWidth = DesiredItemWidth;

            int columns = (int)Math.Floor(availableWidth / DesiredItemWidth);
            if (columns < 1) columns = 1;

            ViewModel.UpdateGridColumns(columns);
        }

        private void VideoCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is VideoItem item)
            {
                ViewModel?.ThumbnailLoader.RequestThumbnail(item);
            }
        }

        private void VideoCard_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is VideoItem item)
            {
                ViewModel?.ThumbnailLoader.CancelRequest(item);
            }
        }

        private void VideoCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is VideoItem item)
            {
                if (e.ClickCount == 2)
                {
                    ViewModel?.HandleVideoDoubleClick(item);
                    e.Handled = true;
                    return;
                }

                bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                
                ViewModel?.HandleVideoClick(item, isCtrl, isShift);
                
                if (!VideosVirtualListBox.IsFocused)
                {
                    VideosVirtualListBox.Focus();
                }
                
                e.Handled = true;
            }
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row && row.DataContext is VideoItem item)
            {
                ViewModel?.HandleVideoDoubleClick(item);
                e.Handled = true;
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && ViewModel != null)
            {
                if (grid.SelectedItems.Count > 0)
                {
                    ViewModel.UpdateSelection(grid.SelectedItems.Cast<VideoItem>());
                }
                else
                {
                    ViewModel.UpdateSelection(Array.Empty<VideoItem>());
                }
            }
        }
    }
}
