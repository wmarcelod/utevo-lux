using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UtevoLux.Features.Map;

/// <summary>
/// Animates multi-frame GIFs on WPF <see cref="Image"/> elements. WPF's <see cref="BitmapImage"/>
/// only ever shows frame 0, so creature/item GIFs looked static; this cycles their frames.
///
/// One shared <see cref="DispatcherTimer"/> advances every registered image (cheap: a handful of
/// tiny sprites), stepping each by real elapsed time against its per-frame delays. Registration is
/// self-cleaning — an image unregisters itself on <see cref="FrameworkElement.Unloaded"/>, so a
/// rebuilt loot grid drops its old cells automatically. Decoded frames are cached per file path.
///
/// Pausable via <see cref="Paused"/> so the map can stop it while unfocused/hidden, matching the
/// idle-GPU policy used for the marker pulses.
/// </summary>
public sealed class GifAnimator
{
    private sealed class Entry
    {
        public Image Img = null!;
        public BitmapSource[] Frames = null!;
        public int[] Delays = null!;
        public int Index;
        public double AccMs;
    }

    private readonly List<Entry> _entries = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastMs;
    private bool _paused;

    private static readonly Dictionary<string, (BitmapSource[] Frames, int[] Delays)> DecodeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DecodeGate = new();

    public GifAnimator()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33.0) };
        _timer.Tick += OnTick;
    }

    /// <summary>Pause/resume every animation (used when the map is not the focused, visible window).</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;
            _paused = value;
            if (value)
            {
                _timer.Stop();
            }
            else if (_entries.Count > 0)
            {
                _lastMs = _clock.ElapsedMilliseconds;
                _timer.Start();
            }
        }
    }

    /// <summary>
    /// Sets <paramref name="img"/>'s source to the GIF/PNG at <paramref name="path"/> and animates it
    /// if it has more than one frame. Returns true when it is actually animating. Replaces any prior
    /// registration for the same image, so re-showing a persistent icon is safe.
    /// </summary>
    public bool Register(Image img, string path)
    {
        _entries.RemoveAll(e => ReferenceEquals(e.Img, img));

        (BitmapSource[] frames, int[] delays) = Decode(path);
        if (frames.Length == 0)
        {
            img.Source = null;
            return false;
        }

        img.Source = frames[0];
        if (frames.Length == 1)
            return false; // static: nothing to animate

        _entries.Add(new Entry { Img = img, Frames = frames, Delays = delays });
        img.Unloaded -= OnImageUnloaded;
        img.Unloaded += OnImageUnloaded;

        if (!_paused && !_timer.IsEnabled)
        {
            _lastMs = _clock.ElapsedMilliseconds;
            _timer.Start();
        }
        return true;
    }

    private void OnImageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Image img)
            return;
        img.Unloaded -= OnImageUnloaded;
        _entries.RemoveAll(x => ReferenceEquals(x.Img, img));
        if (_entries.Count == 0)
            _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        long now = _clock.ElapsedMilliseconds;
        double dt = now - _lastMs;
        _lastMs = now;
        if (dt <= 0)
            return;

        for (int i = 0; i < _entries.Count; i++)
        {
            Entry en = _entries[i];
            en.AccMs += dt;
            int guard = 0;
            while (en.AccMs >= en.Delays[en.Index] && guard++ <= en.Frames.Length)
            {
                en.AccMs -= en.Delays[en.Index];
                en.Index = (en.Index + 1) % en.Frames.Length;
            }
            en.Img.Source = en.Frames[en.Index];
        }
    }

    /// <summary>Decode all frames + per-frame delays (ms), cached per path. Non-GIF/1-frame → single frame.</summary>
    private static (BitmapSource[], int[]) Decode(string path)
    {
        lock (DecodeGate)
        {
            if (DecodeCache.TryGetValue(path, out (BitmapSource[] Frames, int[] Delays) cached))
                return cached;
        }

        var frames = new List<BitmapSource>();
        var delays = new List<int>();
        try
        {
            var decoder = new GifBitmapDecoder(new Uri(path, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (BitmapFrame frame in decoder.Frames)
            {
                BitmapSource bs = frame;
                if (bs.CanFreeze && !bs.IsFrozen)
                    bs.Freeze();
                frames.Add(bs);

                int delayMs = 100;
                try
                {
                    if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("/grctlext/Delay"))
                    {
                        object? raw = md.GetQuery("/grctlext/Delay");
                        if (raw != null)
                        {
                            int centis = Convert.ToInt32(raw);
                            delayMs = centis > 0 ? centis * 10 : 100;
                        }
                    }
                }
                catch
                {
                    delayMs = 100;
                }
                delays.Add(delayMs);
            }
        }
        catch
        {
            // Not a GIF (e.g. a static .png item) — load it as a single frozen frame.
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                frames.Clear();
                delays.Clear();
                frames.Add(bmp);
                delays.Add(100);
            }
            catch
            {
                // give up: return an empty set (caller falls back to a text chip)
            }
        }

        (BitmapSource[], int[]) result = (frames.ToArray(), delays.ToArray());
        lock (DecodeGate)
        {
            DecodeCache[path] = result;
        }
        return result;
    }
}
