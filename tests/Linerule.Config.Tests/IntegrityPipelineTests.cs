using Linerule.Config;
using Linerule.Core;

namespace Linerule.Config.Tests;

public sealed class IntegrityPipelineTests
{
    [Fact]
    public void Empty_input_yields_default_config_5way_product()
    {
        var result = ConfigLoader.ParseString(string.Empty);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(result);
        Assert.Equal(UserConfig.Default, ok.Value);
        Assert.Equal(InputConfig.Default, ok.Value.Input);
        Assert.Equal(HudConfig.Default, ok.Value.Hud);
        Assert.Equal(RenderConfig.Default, ok.Value.Render);
    }

    [Fact]
    public void Round_trip_of_defaults_via_print_yields_default()
    {
        var rendered = TomlPrinter.Render(UserConfig.Default);
        var parsed = ConfigLoader.ParseString(rendered);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(parsed);
        Assert.Equal(UserConfig.Default, ok.Value);
    }

    [Fact]
    public void Unknown_top_level_key_yields_warning_not_fatal()
    {
        const string Input = """
            [frobnicate]
            x = 1
            """;
        var result = ConfigLoader.ParseString(Input);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(result);
        _ = ok;
    }

    [Fact]
    public void Unknown_nested_key_in_hud_yields_warning_path_includes_section()
    {
        const string Input = """
            [hud]
            base_opacty = 0.5
            """;
        var result = ConfigLoader.ParseString(Input);
        var ok = Assert.IsType<Result<UserConfig, ConfigError>.Ok>(result);
        Assert.Equal(HudConfig.Default.BaseOpacity, ok.Value.Hud.BaseOpacity);
    }

    [Fact]
    public void Out_of_range_hud_base_opacity_yields_error_diagnostic_with_dotpath()
    {
        const string Input = """
            [hud]
            base_opacity = 1.5
            """;
        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        var hit = Assert.Single(
            diags.Items,
            d => string.Equals(d.DotPath, "hud.base_opacity", StringComparison.Ordinal)
        );
        Assert.Equal(DiagnosticSeverity.Error, hit.Severity);
        Assert.NotNull(hit.Suggestion);
    }

    [Fact]
    public void Out_of_range_repeat_initial_delay_yields_error_diagnostic()
    {
        const string Input = """
            [input.repeat]
            initial_delay_ms = 3000
            """;
        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        Assert.Contains(
            diags.Items,
            d => string.Equals(d.DotPath, "input.repeat.initial_delay_ms", StringComparison.Ordinal) && d.IsError
        );
    }

    [Fact]
    public void CrossField_hud_padding_too_wide_yields_error_with_related()
    {
        // Default width is 520; edge 261 × 2 = 522 > 520, so this trips the invariant.
        const string Input = """
            [hud.padding]
            edge = 261.0
            """;
        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        var hit = Assert.Single(
            diags.Items,
            d =>
                d.IsError
                && string.Equals(d.DotPath, "hud.padding.edge", StringComparison.Ordinal)
                && d.Related is { Count: > 0 }
        );
        Assert.Contains("hud.width_logical", hit.Related!);
    }

    [Fact]
    public void CrossField_foreground_equals_background_yields_error()
    {
        const string Input = """
            [hud.colors]
            background = { r = 0x42, g = 0x42, b = 0x42, a = 0xFF }
            foreground = { r = 0x42, g = 0x42, b = 0x42, a = 0xFF }
            """;
        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        Assert.Contains(
            diags.Items,
            d => d.IsError && string.Equals(d.DotPath, "hud.colors.foreground", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void CrossField_duplicate_hotkey_chord_yields_error()
    {
        const string Input = """
            [hotkeys]
            cycle_mode = "Ctrl+Alt+R"
            toggle_visible = "Ctrl+Alt+R"
            thicker = "Ctrl+Alt+]"
            thinner = "Ctrl+Alt+["
            more_opaque = "Ctrl+Alt+="
            less_opaque = "Ctrl+Alt+-"
            quit = "Ctrl+Alt+Q"
            """;
        var result = ConfigLoader.ParseString(Input);
        var err = Assert.IsType<Result<UserConfig, ConfigError>.Err>(result);
        var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
        Assert.Contains(diags.Items, d => d.IsError && d.Message.Contains("Ctrl+Alt+R", StringComparison.Ordinal));
    }

    [Fact]
    public void FileIntegrity_compute_sha256_is_deterministic_and_hex_lowercase()
    {
        var a = FileIntegrity.ComputeSha256Hex("hello");
        var b = FileIntegrity.ComputeSha256Hex("hello");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
        Assert.Equal(a, a.ToLowerInvariant());
    }

    [Fact]
    public void FileIntegrity_read_missing_file_returns_filesystem_error()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"linerule-test-{Guid.NewGuid():N}.toml");
        var result = FileIntegrity.Read(bogus);
        var err = Assert.IsType<Result<FileIntegrity.FileIntegrityOk, ConfigError>.Err>(result);
        Assert.IsType<ConfigError.FileSystem>(err.Error);
    }

    [Fact]
    public void FileIntegrity_read_oversize_returns_schema_diagnostic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linerule-test-{Guid.NewGuid():N}.toml");
        try
        {
            // 2 MB of dummy content (cap is 1 MB)
            File.WriteAllBytes(path, new byte[2 * 1024 * 1024]);
            var result = FileIntegrity.Read(path);
            var err = Assert.IsType<Result<FileIntegrity.FileIntegrityOk, ConfigError>.Err>(result);
            var diags = Assert.IsType<ConfigError.SchemaDiagnostics>(err.Error);
            Assert.Contains(
                diags.Items,
                d =>
                    d.Message.Contains("size cap", StringComparison.OrdinalIgnoreCase)
                    || d.Message.Contains("byte", StringComparison.OrdinalIgnoreCase)
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FileIntegrity_sidecar_match_yields_no_warnings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linerule-test-{Guid.NewGuid():N}.toml");
        var sidecar = path + FileIntegrity.SidecarExtension;
        try
        {
            const string Content = "[hud]\nbase_opacity = 0.5\n";
            File.WriteAllText(path, Content);
            File.WriteAllText(sidecar, FileIntegrity.ComputeSha256Hex(Content) + "\n");
            var result = FileIntegrity.Read(path);
            var ok = Assert.IsType<Result<FileIntegrity.FileIntegrityOk, ConfigError>.Ok>(result);
            Assert.Empty(ok.Value.Warnings);
        }
        finally
        {
            File.Delete(path);
            File.Delete(sidecar);
        }
    }

    [Fact]
    public void FileIntegrity_sidecar_mismatch_yields_warning_not_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linerule-test-{Guid.NewGuid():N}.toml");
        var sidecar = path + FileIntegrity.SidecarExtension;
        try
        {
            File.WriteAllText(path, "[hud]\nbase_opacity = 0.5\n");
            File.WriteAllText(sidecar, new string('0', 64) + "\n");
            var result = FileIntegrity.Read(path);
            var ok = Assert.IsType<Result<FileIntegrity.FileIntegrityOk, ConfigError>.Ok>(result);
            var w = Assert.Single(ok.Value.Warnings);
            Assert.Equal(DiagnosticSeverity.Warning, w.Severity);
            Assert.Contains("SHA-256", w.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
            File.Delete(sidecar);
        }
    }
}
