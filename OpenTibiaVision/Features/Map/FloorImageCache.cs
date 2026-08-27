using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Stitches each floor's 256x256 tiles into a single frozen <see cref="BitmapSource"/> off the
/// UI thread, with a 3-entry LRU and in-flight de-duplication (concurrent requests for the same
/// floor share one <see cref="Task"/>). The result is frozen so it can cross threads and bind
/// straight into the UI. Ported faithfully from the original TibiaVision.
/// </summary>
public class FloorImageCache
{
    private const int Capacity = 3;

    private readonly MapTileIndex _index;

    private readonly Dictionary<int, BitmapSource> _cache = new Dictionary<int, BitmapSource>();

    private readonly LinkedList<int> _recency = new LinkedList<int>();

    private readonly Dictionary<int, Task<BitmapSource>> _inFlight = new Dictionary<int, Task<BitmapSource>>();

    private readonly object _gate = new object();

    public FloorImageCache(MapTileIndex index)
    {
        _index = index;
    }

    public Task<BitmapSource> GetFloorAsync(int z)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(z, out var value))
            {
                Touch(z);
                return Task.FromResult(value);
            }
            if (_inFlight.TryGetValue(z, out var value2))
            {
                return value2;
            }
            Task<BitmapSource> task = Task.Run(() => StitchFloor(z));
            _inFlight[z] = task;
            task.ContinueWith(delegate(Task<BitmapSource> t)
            {
                lock (_gate)
                {
                    _inFlight.Remove(z);
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        _cache[z] = t.Result;
                        Touch(z);
                        EvictIfNeeded();
                    }
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    private void Touch(int z)
    {
        _recency.Remove(z);
        _recency.AddFirst(z);
    }

    private void EvictIfNeeded()
    {
        while (_cache.Count > 3 && _recency.Last != null)
        {
            int value = _recency.Last.Value;
            _recency.RemoveLast();
            _cache.Remove(value);
        }
    }

    private BitmapSource StitchFloor(int z)
    {
        MapBounds bounds = _index.Bounds;
        int pixelWidth = Math.Max(bounds.Width, 1);
        int pixelHeight = Math.Max(bounds.Height, 1);
        WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
        int num = 1024;
        byte[] pixels = new byte[num * 256];
        foreach (MapTileIndex.TileRef item in _index.GetTilesForFloor(z))
        {
            try
            {
                BitmapSource? bitmapSource;
                using (FileStream bitmapStream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmapSource = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames.FirstOrDefault();
                }
                if (bitmapSource != null && bitmapSource.PixelWidth == 256 && bitmapSource.PixelHeight == 256)
                {
                    ((bitmapSource.Format == PixelFormats.Bgr32) ? bitmapSource : new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgr32, null, 0.0)).CopyPixels(pixels, num, 0);
                    var (x, y) = bounds.WorldToPixel(item.WorldX, item.WorldY);
                    writeableBitmap.WritePixels(new Int32Rect(x, y, 256, 256), pixels, num, 0);
                }
            }
            catch
            {
            }
        }
        writeableBitmap.Freeze();
        return writeableBitmap;
    }
}
