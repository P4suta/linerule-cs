namespace Linerule.Core.Tests.Unit;

public sealed class TapStepConfigTests
{
    [Fact]
    public void Default_pins_thickness_and_opacity_unit_steps()
    {
        // These are user-visible defaults: every single tap of the
        // thickness/opacity hotkeys moves by these amounts. Changing
        // either value is a UX decision (see ADR-0016 for opacity's
        // perceptual rationale), so the test pins the contract and
        // forces the change to land here in the same PR.
        var d = TapStepConfig.Default;
        Assert.Equal(8, d.Thickness);
        Assert.Equal(4, d.Opacity);
    }

    [Fact]
    public void Record_equality_is_structural()
    {
        var a = new TapStepConfig(Thickness: 8, Opacity: 4);
        var b = new TapStepConfig(Thickness: 8, Opacity: 4);
        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { Opacity = 2 });
    }
}
