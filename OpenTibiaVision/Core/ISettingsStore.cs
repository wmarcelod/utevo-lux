using System;

namespace OpenTibiaVision.Core;

/// <summary>
/// Key/value JSON store backed by an atomic, debounced file (see <see cref="AtomicJsonFile"/>).
/// Values are serialized per key; writes coalesce over 400 ms. This is the shared persistence
/// surface every feature module uses instead of touching disk directly.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Root directory of this store's file (for diagnostics / sibling files).</summary>
    string RootDirectory { get; }

    /// <summary>The backing file path.</summary>
    string FilePath { get; }

    /// <summary>Read and deserialize the value at <paramref name="key"/>, or return the fallback.</summary>
    T Get<T>(string key, T fallback);

    bool TryGet<T>(string key, out T value);

    /// <summary>Set a value (debounced write). Pass null to remove the key.</summary>
    void Set<T>(string key, T value);

    void Remove(string key);

    bool Contains(string key);

    /// <summary>Force any pending write to disk now.</summary>
    void Flush();
}
