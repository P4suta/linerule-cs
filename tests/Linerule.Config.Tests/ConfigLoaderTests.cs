using Linerule.Config;
using Linerule.Core;

namespace Linerule.Config.Tests;

public sealed class ConfigLoaderTests
{
    [Fact]
    public void Empty_input_yields_default_config()
    {
        var result = ConfigLoader.ParseString(string.Empty);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(result);
        Assert.Equal(UserConfig.Default, ok.Value);
    }

    [Fact]
    public void Full_round_trip_with_defaults_matches_UserConfig_Default()
    {
        const string Input = """
            [overlay]
            mask_color = { r = 0, g = 0, b = 0, a = 204 }
            thickness = 28
            opacity = 170

            [hotkeys]
            cycle_mode = "Ctrl+Alt+R"
            toggle_visible = "Ctrl+Alt+H"
            thicker = "Ctrl+Alt+]"
            thinner = "Ctrl+Alt+["
            more_opaque = "Ctrl+Alt+="
            less_opaque = "Ctrl+Alt+-"
            quit = "Ctrl+Alt+Q"
            """;

        var result = ConfigLoader.ParseString(Input);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(result);
        Assert.Equal(UserConfig.Default, ok.Value);
    }

    [Fact]
    public void Invalid_opacity_yields_diagnostic()
    {
        const string Input = """
            [overlay]
            opacity = 0
            """;

        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        Assert.NotEmpty(diags.Items);
        Assert.Contains("opacity", diags.Items[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Syntax_error_yields_diagnostic()
    {
        const string Input = "[overlay\nthickness = 28";

        var result = ConfigLoader.ParseString(Input, sourcePath: "test.toml");
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        Assert.NotEmpty(diags.Items);
    }

    [Fact]
    public void Default_path_includes_linerule_subfolder()
    {
        var path = ConfigLoader.DefaultPath();
        Assert.Contains("linerule", path, StringComparison.Ordinal);
        Assert.EndsWith("config.toml", path, StringComparison.Ordinal);
    }
}
