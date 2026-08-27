using System;
using System.IO;
using System.Threading;

namespace OpenTibiaVision.Core;

/// <summary>
/// Crash-safe single-file persistence (optimization principle 7). Writes go
/// tmp -> copy current to .bak -> atomic File.Move overwrite, all under a lock; reads fall
/// back to the .bak if the primary file is missing/corrupt. Bursty writes are coalesced by a
/// 400 ms one-shot debounce timer so a slider/drag storm collapses into one disk write.
///
/// This type owns only raw text; JSON shaping lives in the caller (SettingsStore / profiles).
/// </summary>
internal sealed class AtomicJsonFile : IDisposable
{
    private const int DebounceMs = 400;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _tmpPath;
    private readonly string _bakPath;
    private readonly Timer _debounce;

    private string? _pendingContent;
    private bool _disposed;

    public AtomicJsonFile(string path)
    {
        _path = path;
        _tmpPath = path + ".tmp";
        _bakPath = path + ".bak";
        _debounce = new Timer(_ => FlushPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public string Path => _path;

    /// <summary>Reads the current content, falling back to the .bak. Returns null if neither exists.</summary>
    public string? ReadRaw()
    {
        lock (_gate)
        {
            // A flush may be pending in memory; that is the freshest content.
            if (_pendingContent is not null)
                return _pendingContent;

            try
            {
                if (File.Exists(_path))
                    return File.ReadAllText(_path);
            }
            catch
            {
                // fall through to .bak
            }

            try
            {
                if (File.Exists(_bakPath))
                    return File.ReadAllText(_bakPath);
            }
            catch
            {
                // nothing readable
            }

            return null;
        }
    }

    /// <summary>Queue a write, coalesced over a 400 ms window.</summary>
    public void QueueWrite(string content)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _pendingContent = content;
            _debounce.Change(DebounceMs, Timeout.Infinite);
        }
    }

    /// <summary>Write any pending content immediately (call on shutdown).</summary>
    public void Flush() => FlushPending();

    private void FlushPending()
    {
        string content;
        lock (_gate)
        {
            if (_pendingContent is null)
                return;
            content = _pendingContent;
            _pendingContent = null;
            _debounce.Change(Timeout.Infinite, Timeout.Infinite);
        }

        try
        {
            lock (_gate)
            {
                string? dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(_tmpPath, content);

                if (File.Exists(_path))
                    File.Copy(_path, _bakPath, overwrite: true);

                File.Move(_tmpPath, _path, overwrite: true);
            }
        }
        catch
        {
            // Best-effort: a locked/full disk must not crash the app. Re-queue so a later
            // edit (or shutdown Flush) retries.
            lock (_gate)
            {
                _pendingContent ??= content;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        FlushPending();
        _debounce.Dispose();
    }
}
