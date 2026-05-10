using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Linerule.Platform.Windows.Diagnostics;
using Linerule.Platform.Windows.Diagnostics.Internal;

namespace Linerule.Platform.Windows.Tests.Diagnostics;

public sealed class JsonFieldWriterTests
{
    [Fact]
    public void Null_renders_as_json_null()
    {
        Assert.Equal("\"k\":null", Render("k", null));
    }

    [Fact]
    public void String_quoted()
    {
        Assert.Equal("\"k\":\"hello\"", Render("k", "hello"));
    }

    [Fact]
    public void Bool_unquoted()
    {
        Assert.Equal("\"k\":true", Render("k", true));
        Assert.Equal("\"k\":false", Render("k", false));
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(-7, "-7")]
    [InlineData(0, "0")]
    public void Int_unquoted(int value, string expected)
    {
        Assert.Equal($"\"k\":{expected}", Render("k", value));
    }

    [Fact]
    public void Long_unquoted()
    {
        Assert.Equal("\"k\":9999999999", Render("k", 9_999_999_999L));
    }

    [Fact]
    public void Uint_unquoted()
    {
        Assert.Equal("\"k\":4000000000", Render("k", 4_000_000_000u));
    }

    [Fact]
    public void Double_unquoted()
    {
        Assert.Equal("\"k\":3.14", Render("k", 3.14d));
    }

    [Fact]
    public void Guid_quoted_in_D_format()
    {
        var g = new Guid("12345678-1234-1234-1234-123456789abc");
        Assert.Equal("\"k\":\"12345678-1234-1234-1234-123456789abc\"", Render("k", g));
    }

    [Fact]
    public void DateTimeOffset_quoted_in_O_format()
    {
        var dto = new DateTimeOffset(2026, 5, 11, 12, 34, 56, TimeSpan.Zero);
        var rendered = Render("k", dto);
        Assert.StartsWith("\"k\":\"2026-05-11T12:34:56", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_quoted_with_name()
    {
        Assert.Equal("\"k\":\"Warn\"", Render("k", LogLevel.Warn));
    }

    [Fact]
    public void Custom_object_falls_back_to_ToString()
    {
        var obj = new CustomObject();
        Assert.Equal("\"k\":\"custom!\"", Render("k", obj));
    }

    private sealed class CustomObject
    {
        public override string ToString() => "custom!";
    }

    private static string Render(string key, object? value)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            JsonFieldWriter.Write(w, new LogField(key, value));
            w.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());
        // Strip the wrapping {...} so each test asserts just the field
        // form — easier to read than full-document comparisons.
        return json[1..^1];
    }
}
