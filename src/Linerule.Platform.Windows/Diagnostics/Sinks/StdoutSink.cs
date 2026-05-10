using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Linerule.Platform.Windows.Diagnostics.Sinks;

/// <summary>
/// Human-readable colored sink for live console viewing.
/// One line per entry: <c>HH:mm:ss.fff LEVEL subsystem | step | k=v k=v</c>.
/// </summary>
internal sealed class StdoutSink : ILogSink
{
    private static readonly Lock Gate = new();
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly bool _useColor;

    public StdoutSink()
    {
        _out = Console.Out;
        _err = Console.Error;
        _useColor = !Console.IsOutputRedirected
            && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    }

    public void Write(in LogEntry entry)
    {
        var sb = new StringBuilder(128);
        sb.Append(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(' ');
        sb.Append(LevelTag(entry.Level));
        sb.Append(' ');
        sb.Append(entry.Subsystem.PadRight(14));
        sb.Append("| ");
        sb.Append(entry.Step);
        if (!entry.Fields.IsDefaultOrEmpty)
        {
            sb.Append(" | ");
            for (var i = 0; i < entry.Fields.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                var f = entry.Fields[i];
                sb.Append(f.Key);
                sb.Append('=');
                sb.Append(FormatValue(f.Value));
            }
        }
        if (entry.Exception is not null)
        {
            sb.Append(' ').Append('|').Append(' ');
            sb.Append(entry.Exception.GetType().Name);
            sb.Append(": ");
            sb.Append(entry.Exception.Message);
        }

        var line = sb.ToString();
        var sink = entry.Level >= LogLevel.Warn ? _err : _out;

        lock (Gate)
        {
            if (_useColor)
            {
                var prev = Console.ForegroundColor;
                Console.ForegroundColor = ColorFor(entry.Level);
                sink.WriteLine(line);
                Console.ForegroundColor = prev;
            }
            else
            {
                sink.WriteLine(line);
            }
            sink.Flush();
        }
    }

    public void Flush()
    {
        lock (Gate)
        {
            _out.Flush();
            _err.Flush();
        }
    }

    private static string LevelTag(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "?????",
    };

    private static ConsoleColor ColorFor(LogLevel l) => l switch
    {
        LogLevel.Trace => ConsoleColor.DarkGray,
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Info => ConsoleColor.White,
        LogLevel.Warn => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        _ => ConsoleColor.White,
    };

    private static string FormatValue(object? v) => v switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "null",
    };
}
