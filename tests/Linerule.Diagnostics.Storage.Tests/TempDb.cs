using System;
using System.IO;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// Per-test scratch DB path. Each test materializes its own file under
/// <c>%TEMP%/linerule-cs-tests/&lt;guid&gt;/events.sqlite</c> so parallel
/// xunit collections cannot clash on the shared sqlite WAL file. The
/// directory is reaped on <see cref="Dispose"/>; failures in cleanup are
/// swallowed because a test that itself failed must never be masked by a
/// teardown exception.
///
/// <para>
/// We deliberately avoid <see cref="Path.GetTempFileName"/> because it
/// creates a zero-byte file at the target path — sqlite then refuses to
/// initialize the WAL against a non-sqlite file with the same name on
/// some Windows file systems. Owning a fresh subdirectory sidesteps that.
/// </para>
/// </summary>
internal sealed partial class TempDb : IDisposable
{
    public TempDb()
    {
        Directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "linerule-cs-tests",
            Guid.NewGuid().ToString("N")
        );
        System.IO.Directory.CreateDirectory(Directory);
        Path = System.IO.Path.Combine(Directory, "events.sqlite");
    }

    public string Directory { get; }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort: a stray WAL/SHM file held by an unrelated reader
            // is not actionable from teardown.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — antivirus / indexer briefly latched the file. Leaving
            // the dir is harmless; the OS reaps %TEMP% routinely.
        }
    }
}
