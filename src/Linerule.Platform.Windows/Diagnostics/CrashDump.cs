using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Linerule.Platform.Windows.Diagnostics.Internal;

namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// Captures unhandled exceptions and process-exit signals into a JSON
/// post-mortem at <c>%TEMP%\linerule-crash-{yyyyMMdd-HHmmss-fff}.json</c>.
/// The dump bundles:
/// <list type="bullet">
///   <item>The terminating exception (type / message / stack / inner chain)</item>
///   <item>The last <see cref="Logger.RecentEntries"/> ring (default 200) — the
///         breadcrumb trail leading to the crash</item>
///   <item>The current <c>GetLastWin32Error</c> snapshot</item>
///   <item>Process metadata (RunId, machine, OS, working set, uptime)</item>
///   <item>Optional caller-supplied state snapshot (cursor position, mode,
///         lifecycle tag — registered via <see cref="RegisterStateSnapshot"/>)</item>
/// </list>
///
/// <para>
/// <b>Why this matters</b>: WinAppSDK / Composition / native FFI code can
/// terminate the process via SEH or AccessViolation paths that bypass
/// normal <c>try/catch</c>. The crash dump is the only post-mortem we
/// reliably get — a missing breadcrumb here is the difference between
/// "fixed in 5 minutes" and "spent the afternoon adding logs."
/// </para>
/// </summary>
public static class CrashDump
{
    private static readonly TimeProvider Time = TimeProvider.System;
    private static readonly Lock DumpGate = new();

    private static int _installed;
    private static Func<LogField[]>? _stateSnapshot;
    private static LoggerHandle? _log;

    /// <summary>
    /// Wire <see cref="AppDomain.UnhandledException"/>,
    /// <see cref="TaskScheduler.UnobservedTaskException"/>, and
    /// <see cref="AppDomain.ProcessExit"/>. Idempotent.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        _log = Logger.For(Subsystems.CrashDump);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        AppDomain.CurrentDomain.ProcessExit += OnExit;

        _log.Info(
            "installed",
            new LogField("dump_dir", Path.GetTempPath()),
            new LogField("ring_capacity", Logger.RingCapacity)
        );
    }

    /// <summary>
    /// Register a callback that captures a serializable snapshot of the
    /// current app state (mode, lifecycle, cursor, etc.). The crash dump
    /// invokes it under the dump gate.
    /// </summary>
    public static void RegisterStateSnapshot(Func<LogField[]> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _stateSnapshot = snapshot;
    }

    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _log?.Error("AppDomain.UnhandledException", ex, new LogField("terminating", e.IsTerminating));
        Dump("AppDomain.UnhandledException", ex, terminating: e.IsTerminating);
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("TaskScheduler.UnobservedTaskException", e.Exception);
        Dump("TaskScheduler.UnobservedTaskException", e.Exception, terminating: false);
        e.SetObserved();
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        _log?.Info("ProcessExit");
        // Logger.Shutdown is wired to ProcessExit independently.
    }

    /// <summary>
    /// Write a dump file. Public so callers can manually snapshot at
    /// strategic points (e.g. before a known-risky operation).
    /// </summary>
    public static string? Dump(string trigger, Exception? exception, bool terminating)
    {
        lock (DumpGate)
        {
            try
            {
                return WriteDumpFile(trigger, exception, terminating);
            }
            catch (IOException ex)
            {
                _log?.Error("dump write failed", ex);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _log?.Error("dump write denied", ex);
                return null;
            }
            catch (Exception ex)
            {
                // Last-resort: never let a crash-dump failure crash the
                // crash-dump path itself.
                _log?.Error("dump write unexpected", ex);
                return null;
            }
        }
    }

    private static string WriteDumpFile(string trigger, Exception? exception, bool terminating)
    {
        var stamp = Time.GetUtcNow();
        var name = string.Create(CultureInfo.InvariantCulture, $"linerule-crash-{stamp:yyyyMMdd-HHmmss-fff}.json");
        var path = Path.Combine(Path.GetTempPath(), name);

        var lastWin32 = Marshal.GetLastWin32Error();
        var entries = Logger.RecentEntries();
        var stateFields = SafeStateSnapshot();

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        w.WriteStartObject();
        WriteHeader(w, trigger, stamp, terminating, lastWin32);
        WriteProcess(w);
        WriteExceptionMaybe(w, exception);
        WriteStateSnapshot(w, stateFields);
        WriteRecentLog(w, entries);
        w.WriteEndObject();
        w.Flush();

        _log?.Warn(
            "dump written",
            new LogField("path", path),
            new LogField("entries", entries.Count),
            new LogField("trigger", trigger)
        );

        return path;
    }

    private static void WriteHeader(
        Utf8JsonWriter w,
        string trigger,
        DateTimeOffset stamp,
        bool terminating,
        int lastWin32
    )
    {
        w.WriteString("trigger", trigger);
        w.WriteString("timestamp", stamp.ToString("O", CultureInfo.InvariantCulture));
        w.WriteBoolean("terminating", terminating);
        w.WriteString("run_id", Logger.RunId.ToString("D"));
        w.WriteNumber("last_win32_error", lastWin32);
        w.WriteString("last_win32_error_name", Win32Guard.DecodeName(lastWin32));
    }

    private static void WriteProcess(Utf8JsonWriter w)
    {
        w.WritePropertyName("process");
        w.WriteStartObject();
        w.WriteString("machine", Environment.MachineName);
        w.WriteString("os_version", Environment.OSVersion.VersionString);
        w.WriteString("clr_version", Environment.Version.ToString());
        w.WriteNumber("processor_count", Environment.ProcessorCount);
        w.WriteNumber("working_set_bytes", Environment.WorkingSet);
        w.WriteNumber("tick_count_ms", Environment.TickCount64);
        w.WriteEndObject();
    }

    private static void WriteExceptionMaybe(Utf8JsonWriter w, Exception? ex)
    {
        if (ex is null)
        {
            return;
        }
        w.WritePropertyName("exception");
        WriteExceptionTree(w, ex);
    }

    private static void WriteExceptionTree(Utf8JsonWriter w, Exception ex)
    {
        w.WriteStartObject();
        w.WriteString("type", ex.GetType().FullName);
        w.WriteString("message", ex.Message);
        w.WriteString("stack", ex.StackTrace ?? string.Empty);
        if (ex is Win32Exception w32)
        {
            w.WriteNumber("native_error_code", w32.NativeErrorCode);
            w.WriteString("native_error_name", Win32Guard.DecodeName(w32.NativeErrorCode));
        }
        if (ex.InnerException is { } inner)
        {
            w.WritePropertyName("inner");
            WriteExceptionTree(w, inner);
        }
        w.WriteEndObject();
    }

    private static void WriteStateSnapshot(Utf8JsonWriter w, LogField[]? fields)
    {
        if (fields is null || fields.Length == 0)
        {
            return;
        }
        w.WritePropertyName("state_snapshot");
        w.WriteStartObject();
        foreach (var f in fields)
        {
            JsonFieldWriter.Write(w, f);
        }
        w.WriteEndObject();
    }

    private static void WriteRecentLog(Utf8JsonWriter w, IReadOnlyList<LogEntry> entries)
    {
        w.WritePropertyName("recent_log");
        w.WriteStartArray();
        foreach (var e in entries)
        {
            WriteLogEntry(w, e);
        }
        w.WriteEndArray();
    }

    private static void WriteLogEntry(Utf8JsonWriter w, in LogEntry entry)
    {
        w.WriteStartObject();
        w.WriteString("ts", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        w.WriteString("level", entry.Level.ToString());
        w.WriteString("subsystem", entry.Subsystem);
        w.WriteString("step", entry.Step);
        if (!entry.Fields.IsDefaultOrEmpty)
        {
            w.WritePropertyName("fields");
            w.WriteStartObject();
            foreach (var f in entry.Fields)
            {
                JsonFieldWriter.Write(w, f);
            }
            w.WriteEndObject();
        }
        if (entry.Exception is { } ex)
        {
            w.WritePropertyName("exception");
            WriteExceptionTree(w, ex);
        }
        w.WriteEndObject();
    }

    private static LogField[]? SafeStateSnapshot()
    {
        var snap = _stateSnapshot;
        if (snap is null)
        {
            return null;
        }
        try
        {
            return snap();
        }
        catch (Exception ex)
        {
            _log?.Warn(
                "state snapshot threw",
                new LogField("type", ex.GetType().FullName ?? "?"),
                new LogField("msg", ex.Message)
            );
            return null;
        }
    }
}
