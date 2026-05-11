using System.Globalization;
using Linerule.Bootstrap;
using Linerule.Core;

namespace Linerule.Bootstrap.Tests;

/// <summary>
/// Algebraic laws of <see cref="Phase{TIn, TOut}"/> as a Kleisli arrow in
/// the <c>Result&lt;_, BootstrapError&gt;</c> error monad:
/// associativity of <see cref="PhaseExtensions.Then"/>, left and right
/// identity (<see cref="Phase.Identity{T}"/>), and short-circuit on Err.
/// </summary>
public sealed class PhaseCompositionTests
{
    private static Phase<int, int> Add(int n) =>
        Phase.Pure<int, int>(string.Create(CultureInfo.InvariantCulture, $"+{n}"), x => x + n);

    private static Phase<int, int> Mul(int n) =>
        Phase.Pure<int, int>(string.Create(CultureInfo.InvariantCulture, $"*{n}"), x => x * n);

    private static async ValueTask<Result<TOut, BootstrapError>> Run<TIn, TOut>(Phase<TIn, TOut> phase, TIn input) =>
        await phase.Run(input, default).ConfigureAwait(false);

    [Fact]
    public async Task Pure_phase_returns_input_through_function()
    {
        var r = await Run(Add(7), 3);
        var ok = Assert.IsType<Result<int, BootstrapError>.Ok>(r);
        Assert.Equal(10, ok.Value);
    }

    [Fact]
    public async Task Identity_passes_input_unchanged()
    {
        var r = await Run(Phase.Identity<string>(), "hello");
        var ok = Assert.IsType<Result<string, BootstrapError>.Ok>(r);
        Assert.Equal("hello", ok.Value);
    }

    [Fact]
    public async Task Left_identity_law()
    {
        // identity >=> f == f
        var fOnly = await Run(Add(5), 10);
        var idThenF = await Run(Phase.Identity<int>().Then(Add(5)), 10);
        Assert.Equal(((Result<int, BootstrapError>.Ok)fOnly).Value, ((Result<int, BootstrapError>.Ok)idThenF).Value);
    }

    [Fact]
    public async Task Right_identity_law()
    {
        // f >=> identity == f
        var fOnly = await Run(Add(5), 10);
        var fThenId = await Run(Add(5).Then(Phase.Identity<int>()), 10);
        Assert.Equal(((Result<int, BootstrapError>.Ok)fOnly).Value, ((Result<int, BootstrapError>.Ok)fThenId).Value);
    }

    [Fact]
    public async Task Associativity_law()
    {
        // (f >=> g) >=> h == f >=> (g >=> h)
        var lhs = Add(1).Then(Mul(2)).Then(Add(3));
        var rhs = Add(1).Then(Mul(2).Then(Add(3)));
        var lhsOut = (Result<int, BootstrapError>.Ok)await Run(lhs, 10);
        var rhsOut = (Result<int, BootstrapError>.Ok)await Run(rhs, 10);
        Assert.Equal(lhsOut.Value, rhsOut.Value);
    }

    [Fact]
    public async Task Then_short_circuits_on_first_err()
    {
        var called = false;
        var failing = Phase.FromResult<int, int>(
            "fail",
            _ => Result.Err<int, BootstrapError>(new BootstrapError.PhaseFailed("fail", "boom"))
        );
        var observe = Phase.Pure<int, int>(
            "observe",
            x =>
            {
                called = true;
                return x;
            }
        );
        var r = await Run(failing.Then(observe), 1);
        Assert.IsType<Result<int, BootstrapError>.Err>(r);
        Assert.False(called);
    }

    [Fact]
    public async Task Then_traps_exception_into_BootstrapError_Threw()
    {
        var throwing = Phase.Pure<int, int>("throwing", _ => throw new InvalidOperationException("kaboom"));
        var next = Add(1);
        var r = await Run(throwing.Then(next), 5);
        var err = Assert.IsType<Result<int, BootstrapError>.Err>(r);
        Assert.IsType<BootstrapError.Threw>(err.Error);
    }

    [Fact]
    public async Task Then_reports_cancellation_before_next_phase()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        await cts.CancelAsync();
        var first = Add(1);
        var second = Add(2);
        var r = await first.Then(second).Run(0, cts.Token);
        var err = Assert.IsType<Result<int, BootstrapError>.Err>(r);
        Assert.IsType<BootstrapError.Cancelled>(err.Error);
    }

    [Fact]
    public async Task Map_combinator_threads_through_pure_function()
    {
        var phase = Add(1).Map(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var r = (Result<string, BootstrapError>.Ok)await Run(phase, 41);
        Assert.Equal("42", r.Value);
    }

    [Fact]
    public async Task FromArrow_resolves_async_inner_delegate()
    {
        var phase = Phase.FromArrow<int, int>(
            "async-add-1",
            async (x, _) =>
            {
                await Task.Yield();
                return Result.Ok<int, BootstrapError>(x + 1);
            }
        );
        var r = (Result<int, BootstrapError>.Ok)await Run(phase, 9);
        Assert.Equal(10, r.Value);
    }
}
