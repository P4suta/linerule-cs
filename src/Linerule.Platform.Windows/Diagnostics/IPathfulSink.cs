namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Sink that exposes its on-disk persistence path. <c>LoggerRoot</c> queries
/// this once at construction to stamp <c>ctx.db</c> on crash dumps; the
/// previous implementation went through a reflective property lookup that
/// tripped IL2075 under the AOT analyzer and would have silently returned
/// <see langword="null"/> after any property rename. See ADR-0010.
/// </summary>
internal interface IPathfulSink : ILogSink
{
    string Path { get; }
}
