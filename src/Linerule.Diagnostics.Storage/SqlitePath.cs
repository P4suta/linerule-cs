using System;
using System.IO;

namespace Linerule.Diagnostics.Storage;

/// <summary>
/// Resolves the default on-disk location of the events store.
///
/// <para>
/// On Windows this maps to <c>%APPDATA%\linerule\events.sqlite</c>; on POSIX
/// (where <see cref="Environment.SpecialFolder.ApplicationData"/> follows
/// the XDG spec) it maps to <c>$XDG_CONFIG_HOME/linerule/events.sqlite</c>
/// (or the documented fallback). The CLI may override this via a flag — the
/// default exists so users have a single, discoverable artifact to point
/// DuckDB / sqlite3 / DBeaver at.
/// </para>
/// </summary>
public static class SqlitePath
{
    /// <summary>App-scoped directory name beneath <c>ApplicationData</c>.</summary>
    public const string AppDirName = "linerule";

    /// <summary>Default events-store filename.</summary>
    public const string FileName = "events.sqlite";

    /// <summary>
    /// Resolve the default events-store path. Does NOT create the parent
    /// directory — that's the sink's responsibility (it knows what file
    /// permissions / FileShare semantics it wants).
    /// </summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify
        );
        // ApplicationData can be empty in odd headless setups (e.g. a
        // service account with no profile); fall back to the current dir
        // rather than crashing the bootstrap.
        var root = string.IsNullOrEmpty(appData) ? "." : appData;
        return Path.Combine(root, AppDirName, FileName);
    }
}
