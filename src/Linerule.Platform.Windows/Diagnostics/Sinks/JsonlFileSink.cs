using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Linerule.Platform.Windows.Diagnostics.Internal;

namespace Linerule.Platform.Windows.Diagnostics.Sinks;

/// <summary>
/// JSONL file sink — one JSON object per line, append-only. Stable schema:
/// <code>
/// { "ts":"2026-05-11T18:23:21.518Z", "level":"info", "subsystem":"OverlayWindow",
///   "step":"...", "ctx":{"run_id":"...", ...}, "fields":{...},
///   "exception":{"type":"...","message":"...","stack":"..."} }
/// </code>
/// Designed for <c>jq</c> / <c>vector</c> consumption; downstream tools can
/// pivot on any field. Writes are flushed per entry — crash-safe at the cost
/// of a syscall per line (acceptable at our event volume).
/// </summary>
internal sealed partial class JsonlFileSink : ILogSink, IDisposable
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, SkipValidation = true };

    private readonly Lock _gate = new();
    private readonly StreamWriter _writer;
    private readonly ArrayBufferWriter<byte> _buffer;
    private bool _disposed;

    public string Path { get; }

    /// <summary>
    /// Open the JSONL log. <paramref name="append"/> selects file mode:
    /// <see langword="true"/> appends to the existing file (history
    /// preserved across runs, callers responsible for filtering on
    /// <c>ctx.run_id</c>); <see langword="false"/> truncates so each run
    /// starts with a clean file (typical dev iteration). Default is
    /// truncate — the user just wants to see what THIS run did, and the
    /// crash dumps under <c>%TEMP%\linerule-crash-*.json</c> still
    /// preserve any catastrophic history.
    /// </summary>
    public JsonlFileSink(string path, bool append = false)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        var stream = new FileStream(
            path,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.WriteThrough
        );
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _buffer = new ArrayBufferWriter<byte>(512);
    }

    public void Write(in LogEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                EmitLine(entry);
            }
            catch (IOException)
            {
                // Sink-local failure (disk full, file locked, etc.) must not
                // bubble; the producer logs because something already
                // deserves attention — the logger itself must not become a
                // new failure surface.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    private void EmitLine(in LogEntry entry)
    {
        _buffer.ResetWrittenCount();
        using (var w = new Utf8JsonWriter(_buffer, WriterOptions))
        {
            w.WriteStartObject();
            WriteHeader(w, entry);
            WriteContext(w, entry.Context);
            WriteFields(w, entry);
            WriteException(w, entry.Exception);
            w.WriteEndObject();
        }

        _writer.BaseStream.Write(_buffer.WrittenSpan);
        _writer.BaseStream.WriteByte((byte)'\n');
        _writer.BaseStream.Flush();
    }

    private static void WriteHeader(Utf8JsonWriter w, in LogEntry entry)
    {
        w.WriteString("ts", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        w.WriteString("level", LevelToken(entry.Level));
        w.WriteString("subsystem", entry.Subsystem);
        w.WriteString("step", entry.Step);
    }

    private static void WriteContext(Utf8JsonWriter w, LogContext ctx)
    {
        w.WritePropertyName("ctx");
        w.WriteStartObject();
        w.WriteString("run_id", ctx.RunId.ToString("D"));
        if (ctx.SessionId is { } sid)
        {
            w.WriteString("session_id", sid.ToString("D"));
        }
        if (ctx.FrameSeq is { } fs)
        {
            w.WriteNumber("frame_seq", fs);
        }
        if (ctx.ActivityId is { } aid)
        {
            w.WriteString("activity_id", aid);
        }
        w.WriteEndObject();
    }

    private static void WriteFields(Utf8JsonWriter w, in LogEntry entry)
    {
        if (entry.Fields.IsDefaultOrEmpty)
        {
            return;
        }
        w.WritePropertyName("fields");
        w.WriteStartObject();
        foreach (var f in entry.Fields)
        {
            JsonFieldWriter.Write(w, f);
        }
        w.WriteEndObject();
    }

    private static void WriteException(Utf8JsonWriter w, Exception? ex)
    {
        if (ex is null)
        {
            return;
        }
        w.WritePropertyName("exception");
        w.WriteStartObject();
        w.WriteString("type", ex.GetType().FullName);
        w.WriteString("message", ex.Message);
        w.WriteString("stack", ex.StackTrace ?? string.Empty);
        if (ex.InnerException is { } inner)
        {
            w.WritePropertyName("inner");
            w.WriteStartObject();
            w.WriteString("type", inner.GetType().FullName);
            w.WriteString("message", inner.Message);
            w.WriteString("stack", inner.StackTrace ?? string.Empty);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    public void Flush()
    {
        if (_disposed)
        {
            return;
        }
        lock (_gate)
        {
            try
            {
                _writer.BaseStream.Flush();
            }
            catch (IOException)
            {
                // ignore
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        lock (_gate)
        {
            _writer.Dispose();
        }
    }

    private static string LevelToken(LogLevel l) =>
        l switch
        {
            LogLevel.Trace => "trace",
            LogLevel.Debug => "debug",
            LogLevel.Info => "info",
            LogLevel.Warn => "warn",
            LogLevel.Error => "error",
            _ => "info",
        };
}
