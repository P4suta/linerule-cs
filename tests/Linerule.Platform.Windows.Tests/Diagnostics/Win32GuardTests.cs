using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows.Tests.Diagnostics;

public sealed class Win32GuardTests
{
    [Theory]
    [InlineData(0, "ERROR_SUCCESS")]
    [InlineData(2, "ERROR_FILE_NOT_FOUND")]
    [InlineData(5, "ERROR_ACCESS_DENIED")]
    [InlineData(6, "ERROR_INVALID_HANDLE")]
    [InlineData(87, "ERROR_INVALID_PARAMETER")]
    [InlineData(1400, "ERROR_INVALID_WINDOW_HANDLE")]
    [InlineData(1409, "ERROR_HOTKEY_ALREADY_REGISTERED")]
    [InlineData(1410, "ERROR_CLASS_ALREADY_EXISTS")]
    public void DecodeName_returns_symbolic_name_for_known_errors(int code, string expected)
    {
        Assert.Equal(expected, Win32Guard.DecodeName(code));
    }

    [Fact]
    public void DecodeName_for_unknown_code_falls_back_to_OS_message_with_WIN32_prefix()
    {
        // 99999 isn't a defined Win32 error — DecodeName falls back to
        // GetPInvokeErrorMessage, prefixed with WIN32_<code>.
        var name = Win32Guard.DecodeName(99999);
        Assert.StartsWith("WIN32_99999", name, System.StringComparison.Ordinal);
    }
}
