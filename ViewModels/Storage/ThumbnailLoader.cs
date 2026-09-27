using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using RMX3171ControlCentre.Models.Storage;
using RMX3171ControlCentre.Services.Adb;

namespace RMX3171ControlCentre.ViewModels.Storage
{
    public class ThumbnailLoader : IDisposable
    {
        private readonly IAdbService _adbService;
        private readonly string _cacheDir;
        private readonly Dictionary<string, BitmapImage> _memoryCache = new();
        private readonly Dictionary<PhotoItem, CancellationTokenSource> _pendingTasks = new();
        private readonly SemaphoreSlim _semaphore = new(3, 3); // Max 3 concurrent adb pulls
        private const int MaxCacheSize = 300;

        public ThumbnailLoader(IAdbService adbService)
        {
            _adbService = adbService;
            _cacheDir = Path.Combine(Path.GetTempPath(), "RMX3171_Thumbnails");
            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }
            else
            {
                // Clean up any orphaned files from previous crashed sessions
                Task.Run(() => ClearTempFilesSafe());
            }
        }

        private void ClearTempFilesSafe()
        {
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    foreach (var file in Directory.GetFiles(_cacheDir))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        public async void RequestThumbnail(PhotoItem item)
        {
            if (item.Thumbnail != null) return;
            if (_memoryCache.TryGetValue(item.FullPath, out var cached))
            {
                item.Thumbnail = cached;
                return;
            }

            if (_pendingTasks.ContainsKey(item)) return;

            var cts = new CancellationTokenSource();
            _pendingTasks[item] = cts;

            try
            {
                await _semaphore.WaitAsync(cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                string tempFileName = Guid.NewGuid().ToString("N") + Path.GetExtension(item.FileName);
                string localPath = Path.Combine(_cacheDir, tempFileName);

                var pullCmd = $"pull \"{item.FullPath}\" \"{localPath}\"";
                var res = await _adbService.ExecuteCommandAsync(pullCmd, true, cts.Token);

                if (cts.Token.IsCancellationRequested) return;

                if (File.Exists(localPath))
                {
                    BitmapImage? bmp = null;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad; // Load fully so we can delete file
                            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                            bmp.DecodePixelWidth = 240; // Generate 240px thumbnail to save memory
                            bmp.UriSource = new Uri(localPath);
                            bmp.EndInit();
                            bmp.Freeze(); // Cross-thread access
                        }
                        catch
                        {
                            bmp = null;
                        }
                    });

                    if (bmp != null)
                    {
                        ManageCache(item.FullPath, bmp);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            item.Thumbnail = bmp;
                        });
                    }

                    // Delete the temp file now that it's cached in memory
                    try
                    {
                        File.Delete(localPath);
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                _semaphore.Release();
                _pendingTasks.Remove(item);
                cts.Dispose();
            }
        }

        public void CancelRequest(PhotoItem item)
        {
            if (_pendingTasks.TryGetValue(item, out var cts))
            {
                cts.Cancel();
                _pendingTasks.Remove(item);
            }
        }

        public async Task<BitmapImage?> GetFullImageAsync(PhotoItem item, CancellationToken token)
        {
            string tempFileName = Guid.NewGuid().ToString("N") + Path.GetExtension(item.FileName);
            string localPath = Path.Combine(_cacheDir, tempFileName);

            try
            {
                var pullCmd = $"pull \"{item.FullPath}\" \"{localPath}\"";
                var res = await _adbService.ExecuteCommandAsync(pullCmd, true, token);

                token.ThrowIfCancellationRequested();

                if (File.Exists(localPath))
                {
                    BitmapImage? bmp = null;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            int maxDim = 1920;
                            int decodeW = 0;
                            int decodeH = 0;

                            try
                            {
                                using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                {
                                    var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                    if (decoder.Frames.Count > 0)
                                    {
                                        int rawW = decoder.Frames[0].PixelWidth;
                                        int rawH = decoder.Frames[0].PixelHeight;
                                        if (rawW > maxDim || rawH > maxDim)
                                        {
                                            if (rawW >= rawH) decodeW = maxDim;
                                            else decodeH = maxDim;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                decodeW = maxDim; // Fallback to 1920 width
                            }

                            bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                            if (decodeW > 0) bmp.DecodePixelWidth = decodeW;
                            if (decodeH > 0) bmp.DecodePixelHeight = decodeH;
                            bmp.UriSource = new Uri(localPath);
                            bmp.EndInit();
                            bmp.Freeze();
                        }
                        catch
                        {
                            bmp = null;
                        }
                    });
                    return bmp;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
            }

            return null;
        }

        private void ManageCache(string path, BitmapImage bmp)
        {
            lock (_memoryCache)
            {
                if (_memoryCache.Count >= MaxCacheSize)
                {
                    // Evict oldest half
                    var keysToDrop = _memoryCache.Keys.Take(MaxCacheSize / 2).ToList();
                    foreach (var k in keysToDrop)
                    {
                        _memoryCache.Remove(k);
                    }
                }
                _memoryCache[path] = bmp;
            }
        }

        public void Clear()
        {
            foreach (var cts in _pendingTasks.Values)
            {
                cts.Cancel();
            }
            _pendingTasks.Clear();
            _memoryCache.Clear();
            
            ClearTempFilesSafe();
        }

        public void Dispose()
        {
            Clear();
            _semaphore.Dispose();
        }
    }
}
