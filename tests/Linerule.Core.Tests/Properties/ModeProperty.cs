using FsCheck.Xunit;

namespace Linerule.Core.Tests.Properties;

public sealed class ModeProperty
{
    [Property]
    public bool Cycle_is_period_3(byte raw)
    {
        var mode = (Mode)(raw % 3);
        return mode.Cycle().Cycle().Cycle() == mode;
    }

    [Property]
    public bool Cycle_is_a_permutation(byte raw)
    {
        var mode = (Mode)(raw % 3);
        return mode.Cycle() != mode;
    }
}
