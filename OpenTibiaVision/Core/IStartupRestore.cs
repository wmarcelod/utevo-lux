using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTibiaVision.Core;

/// <summary>
/// Optional: a module (or anything the shell knows about) that has saved state to restore at
/// launch. The shell awaits this behind the progress overlay during staggered startup, so the
/// shell is interactive within a frame and restored items appear progressively (principle 6).
/// </summary>
public interface IStartupRestore
{
    Task RestoreAsync(IProgress<string> progress, CancellationToken ct);
}
