using System.Globalization;
using Linerule.Bootstrap;
using Linerule.Core;

namespace Linerule.Bootstrap.Tests;

public sealed class PhaseFactoryTests
{
    [Fact]
    public async Task Pure_lifts_sync_function_into_phase()
    {
        var p = Phase.Pure<int, int>("double", x => x * 2);
        Assert.Equal("double", p.Name);
        var outcome = await p.Run(21, CancellationToken.None);
        var result = Assert.IsType<Result<int, BootstrapError>.Ok>(outcome);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Pure_rejects_null_function()
    {
        Assert.Throws<ArgumentNullException>(() => Phase.Pure<int, int>("noop", null!));
    }

    [Fact]
    public async Task Identity_passes_input_through_unchanged()
    {
        var p = Phase.Identity<string>();
        Assert.Equal("identity", p.Name);
        var outcome = await p.Run("hello", CancellationToken.None);
        var result = Assert.IsType<Result<string, BootstrapError>.Ok>(outcome);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public async Task FromResult_propagates_ok_branch()
    {
        var p = Phase.FromResult<int, string>(
            "toStr",
            x => Result.Ok<string, BootstrapError>(x.ToString(CultureInfo.InvariantCulture))
        );
        var outcome = await p.Run(7, CancellationToken.None);
        var result = Assert.IsType<Result<string, BootstrapError>.Ok>(outcome);
        Assert.Equal("7", result.Value);
    }

    [Fact]
    public async Task FromResult_propagates_err_branch()
    {
        var err = new BootstrapError.PhaseFailed("explicit-error", "deliberate failure");
        var p = Phase.FromResult<int, int>("err", _ => Result.Err<int, BootstrapError>(err));
        var outcome = await p.Run(99, CancellationToken.None);
        var result = Assert.IsType<Result<int, BootstrapError>.Err>(outcome);
        Assert.Equal(err, result.Error);
    }

    [Fact]
    public void FromResult_rejects_null_function()
    {
        Assert.Throws<ArgumentNullException>(() => Phase.FromResult<int, int>("noop", null!));
    }

    [Fact]
    public async Task FromArrow_lifts_async_result_function()
    {
        var p = Phase.FromArrow<int, int>(
            "asyncDouble",
            async (x, _) =>
            {
                await Task.Yield();
                return Result.Ok<int, BootstrapError>(x * 2);
            }
        );
        var outcome = await p.Run(20, CancellationToken.None);
        var result = Assert.IsType<Result<int, BootstrapError>.Ok>(outcome);
        Assert.Equal(40, result.Value);
    }

    [Fact]
    public void FromArrow_rejects_null_function()
    {
        Assert.Throws<ArgumentNullException>(() => Phase.FromArrow<int, int>("noop", null!));
    }
}
