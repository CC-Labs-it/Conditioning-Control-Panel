using System;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PanicAftermathPortTests
{
    [Fact]
    public void StopPassArmsQuietBeforeAnySurfaceEvenIfItThrows()
    {
        var saved = PanicSurfaces.All;
        PanicAftermath.EndQuiet();
        bool observed = false;
        try
        {
            PanicSurfaces.All = new PanicSurfaces.Surface[]
            {
                new("failure", _ => throw new InvalidOperationException()),
                new("probe", _ => observed = PanicAftermath.TriggersSilenced()),
            };
            PanicSurfaces.StopAll("test");
            Assert.True(observed);
            Assert.True(PanicAftermath.HoldsEngineRestart);
        }
        finally { PanicSurfaces.All = saved; PanicAftermath.EndQuiet(); }
    }

    [Fact]
    public void AlreadyArmedKeyDoesNotCountTwice()
    {
        var saved = PanicSurfaces.All;
        try
        {
            PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
            PanicAftermath.ArmQuiet();
            var count = PanicAftermath.PanicCount;
            PanicSurfaces.StopAll("panic key", aftermathArmed: true);
            Assert.Equal(count, PanicAftermath.PanicCount);
            Assert.True(PanicAftermath.TriggersSilenced());
        }
        finally { PanicSurfaces.All = saved; PanicAftermath.EndQuiet(); }
    }
}
