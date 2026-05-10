using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Linerule.Platform.Windows.Diagnostics.Sinks;

/// <summary>
/// JSONL file sink — one JSON object per line, append-only. Stable schema:
/// <code>
/// { "ts":"2026-05-11T18:23:21.518Z", "level":"info", "subsystem":"OverlayWindow",
///   "step":"...", "ctx":{"run_id":"...", "session_id":null, "frame_seq":null,
///   "activity_id":null}, "fields":{...}, "exception":{"type":"...","message":"...","stack":"..."} }
/// </code>
/// Designed for <c>jq</c> / <c>vector</c> consumption; downstream tools can
/// pivot on any field. Writes are flushed per entry — crash-safe at the cost
/// of a syscall per line (acceptable at our event volume).
/// </summary>
internal sealed class JsonlFileSink : ILogSink, IDisposable
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private readonly Lock _gate = new();
    private readonly StreamWriter _writer;
    private readonly ArrayBufferWriter<byte> _buffer;
    private bool _disposed;

    public string Path { get; }

    public JsonlFileSink(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, FileOptions.WriteThrough);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
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
                _buffer.ResetWrittenCount();
                using (var w = new Utf8JsonWriter(_buffer, WriterOptions))
                {
                    w.WriteStartObject();
                    w.WriteString("ts", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
                    w.WriteString("level", LevelToken(entry.Level));
                    w.WriteString("subsystem", entry.Subsystem);
                    w.WriteString("step", entry.Step);

                    w.WritePropertyName("ctx");
                    w.WriteStartObject();
                    w.WriteString("run_id", entry.Context.RunId.ToString("D"));
                    if (entry.Context.SessionId is { } sid)
                    {
                        w.WriteString("session_id", sid.ToString("D"));
                    }
                    if (entry.Context.FrameSeq is { } fs)
                    {
                        w.WriteNumber("frame_seq", fs);
                    }
                    if (entry.Context.ActivityId is { } aid)
                    {
                        w.WriteString("activity_id", aid);
                    }
                    w.WriteEndObject();

                    if (!entry.Fields.IsDefaultOrEmpty)
                    {
                        w.WritePropertyName("fields");
                        w.WriteStartObject();
                        foreach (var f in entry.Fields)
                        {
                            WriteField(w, f);
                        }
                        w.WriteEndObject();
                    }

                    if (entry.Exception is { } ex)
                    {
                        w.WritePropertyName("exception");
                        w.WriteStartObject();
                        w.WriteString("type", ex.GetType().FullName);
                        w.WriteString("message", ex.Message);
                        w.WriteString("stack", ex.StackTrace ?? string.Empty);
                        w.WriteEndObject();
                    }

                    w.WriteEndObject();
                }

                _writer.BaseStream.Write(_buffer.WrittenSpan);
                _writer.BaseStream.WriteByte((byte)'\n');
                _writer.BaseStream.Flush();
            }
            catch (IOException)
            {
                // Sink-local failure (disk full, file locked, etc.) must not
                // bubble; the producer logs because something deserves
                // attention — the logger itself must not become a new
                // failure surface.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
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

    private static string LevelToken(LogLevel l) => l switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warn => "warn",
        LogLevel.Error => "error",
        _ => "info",
    };

    private static void WriteField(Utf8JsonWriter w, LogField f)
    {
        w.WritePropertyName(f.Key);
        switch (f.Value)
        {
            case null:
                w.WriteNullValue();
                break;
            case string s:
                w.WriteStringValue(s);
                break;
            case bool b:
                w.WriteBooleanValue(b);
                break;
            case int i:
                w.WriteNumberValue(i);
                break;
            case long l:
                w.WriteNumberValue(l);
                break;
            case uint u:
                w.WriteNumberValue(u);
                break;
            case ulong ul:
                w.WriteNumberValue(ul);
                break;
            case double d:
                w.WriteNumberValue(d);
                break;
            case float fl:
                w.WriteNumberValue(fl);
                break;
            case decimal dc:
                w.WriteNumberValue(dc);
                break;
            case DateTimeOffset dto:
                w.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                w.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case Enum e:
                w.WriteStringValue(e.ToString());
                break;
            case IFormattable fm:
                w.WriteStringValue(fm.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                w.WriteStringValue(f.Value?.ToString() ?? string.Empty);
                break;
        }
    }
}
