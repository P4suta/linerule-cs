using Linerule.Diagnostics.Storage;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Diagnostics.Storage.Tests;

/// <summary>
/// Regression guard for the ADR-0010 IPathfulSink fix. Prior to the typed
/// dispatch, <c>LoggerRoot.DbPath</c> went through a reflective property
/// lookup that silently returned an empty string when no <c>Path</c>
/// property existed on the sink. That path tripped IL2075 under AOT
/// analysis; the typed <see cref="IPathfulSink"/> route is both AOT-safe
/// and exercises a code path that the previous implementation never
/// reached (since <c>SqliteEventSink</c> had no <c>Path</c> property
/// until this change).
/// </summary>
public sealed class LoggerRootDbPathTests
{
    [Fact]
    public async Task DbPath_returns_sink_path_via_IPathfulSink()
    {
        using var temp = new TempDb();
        var run = RunMetadata.Capture(["dbpath-test"]);
        using var sink = new SqliteEventSink(temp.Path, run);

        await using var root = LoggerRoot.Create(fileSink: sink);

        Assert.Equal(temp.Path, root.DbPath);
    }

    [Fact]
    public async Task DbPath_is_empty_when_no_file_sink()
    {
        // Negative branch — IPathfulSink dispatch returns null → DbPath = "".
        await using var root = LoggerRoot.Create(fileSink: null);

        Assert.Equal(string.Empty, root.DbPath);
    }
}
