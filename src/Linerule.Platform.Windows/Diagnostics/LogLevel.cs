namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Severity ladder. Numeric ordering is the natural filter order:
/// <c>entry.Level &gt;= subsystemFilter</c> ⇒ admit.
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}
