using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class RainSharedCoreTests
{
    [Fact]
    public void WpfUsesTheSharedRainRulesAndKeepsOnlyItsProgressAdapter()
    {
        var core = typeof(BackRoomApi).Assembly;
        Assert.All(new Type[]
        {
            typeof(RainLookCodec), typeof(RainLookClamp), typeof(RainLookHash),
            typeof(RainItemAccess), typeof(RainLookSync),
        }, rule => Assert.Same(core, rule.Assembly));
        Assert.NotSame(core, typeof(AppRainProgressSource).Assembly);
    }
}
