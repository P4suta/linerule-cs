using System;
using System.Globalization;
using System.Text.Json;

namespace Linerule.Platform.Windows.Diagnostics.Internal;

/// <summary>
/// AOT-safe JSON serialization for <see cref="LogField"/> values. Avoids
/// <see cref="JsonSerializer.Serialize{TValue}(Utf8JsonWriter, TValue, JsonSerializerOptions)"/>
/// (which carries <c>RequiresUnreferencedCode</c> and
/// <c>RequiresDynamicCode</c>) by switching on the runtime type of the
/// value and calling the corresponding <see cref="Utf8JsonWriter"/>
/// primitive directly.
///
/// <para>
/// Adding a new "interesting" type (e.g. a domain enum that should
/// serialize as its display name) is a one-line addition to the switch —
/// the closed expansion is intentional, not an open extension point.
/// </para>
/// </summary>
internal static class JsonFieldWriter
{
    public static void Write(Utf8JsonWriter w, LogField field)
    {
        w.WritePropertyName(field.Key);
        WriteValue(w, field.Value);
    }

    public static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
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
            case short sh:
                w.WriteNumberValue(sh);
                break;
            case byte by:
                w.WriteNumberValue(by);
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
            case Guid g:
                w.WriteStringValue(g.ToString("D"));
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
                w.WriteStringValue(fm.ToString(format: null, CultureInfo.InvariantCulture));
                break;
            default:
                w.WriteStringValue(value.ToString() ?? string.Empty);
                break;
        }
    }
}
