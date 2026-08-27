namespace UtevoLux.Core;

/// <summary>
/// Optional: a module that needs to run cleanup on app shutdown (close overlay windows without
/// flipping their persisted state, flush caches, etc.). The shell calls this before the final
/// settings flush.
/// </summary>
public interface IShutdownHook
{
    void Shutdown();
}
