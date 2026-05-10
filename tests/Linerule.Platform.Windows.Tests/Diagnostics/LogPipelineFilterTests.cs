using System.Collections.Generic;
using Linerule.Platform.Windows.Diagnostics;

namespace Linerule.Platform.Windows.Tests.Diagnostics;

public sealed class LogPipelineFilterTests
{
    [Fact]
    public void NullSpec_uses_fallback_default_and_empty_per_subsystem()
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec(null, fallbackDefault: LogLevel.Info);
        Assert.Empty(perSubsystem);
        Assert.Equal(LogLevel.Info, def);
    }

    [Fact]
    public void EmptySpec_uses_fallback_default_and_empty_per_subsystem()
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec(string.Empty, fallbackDefault: LogLevel.Debug);
        Assert.Empty(perSubsystem);
        Assert.Equal(LogLevel.Debug, def);
    }

    [Fact]
    public void StarOverridesDefault()
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec("*=Trace", fallbackDefault: LogLevel.Info);
        Assert.Empty(perSubsystem);
        Assert.Equal(LogLevel.Trace, def);
    }

    [Fact]
    public void PerSubsystemOverridesAreParsed()
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec("OverlayWindow=Trace,WndProc=Debug,*=Warn", LogLevel.Info);
        Assert.Equal(LogLevel.Warn, def);
        Assert.Equal(LogLevel.Trace, perSubsystem["OverlayWindow"]);
        Assert.Equal(LogLevel.Debug, perSubsystem["WndProc"]);
        Assert.Equal(2, perSubsystem.Count);
    }

    [Fact]
    public void CaseInsensitiveLevelNames()
    {
        var (perSubsystem, _) = LogPipeline.ParseFilterSpec("X=trace,Y=DEBUG,Z=Info,W=warn,V=ERROR");
        Assert.Equal(LogLevel.Trace, perSubsystem["X"]);
        Assert.Equal(LogLevel.Debug, perSubsystem["Y"]);
        Assert.Equal(LogLevel.Info, perSubsystem["Z"]);
        Assert.Equal(LogLevel.Warn, perSubsystem["W"]);
        Assert.Equal(LogLevel.Error, perSubsystem["V"]);
    }

    [Fact]
    public void TypoesAreSilentlyDroppedNotThrown()
    {
        // Resilience to typos: a malformed token doesn't poison the
        // whole spec. The valid tokens still apply.
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec("OverlayWindow=Bogus,WndProc=Debug,=Trace,UnknownButValidLevel=Info,*=Warn");
        Assert.Equal(LogLevel.Warn, def);
        Assert.Equal(LogLevel.Debug, perSubsystem["WndProc"]);
        Assert.Equal(LogLevel.Info, perSubsystem["UnknownButValidLevel"]);
        Assert.False(perSubsystem.ContainsKey("OverlayWindow"));  // bad level → dropped
    }

    [Fact]
    public void WhitespaceAroundTokensIsTrimmed()
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec("  OverlayWindow = Trace ,  WndProc=Debug ,*= Info  ");
        Assert.Equal(LogLevel.Info, def);
        Assert.Equal(LogLevel.Trace, perSubsystem["OverlayWindow"]);
        Assert.Equal(LogLevel.Debug, perSubsystem["WndProc"]);
    }

    [Theory]
    [InlineData("=Trace")]    // empty key
    [InlineData("Foo=")]      // empty value
    [InlineData("NoEquals")]  // no '=' at all
    public void MalformedTokensAreIgnored(string token)
    {
        var (perSubsystem, def) = LogPipeline.ParseFilterSpec(token, LogLevel.Info);
        Assert.Empty(perSubsystem);
        Assert.Equal(LogLevel.Info, def);
    }
}
