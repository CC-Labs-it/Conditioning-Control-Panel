using System;
using System.Linq;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionStopPassCountsEachPanicOnce(bool keyAlreadyArmed)
    {
        var saved = PanicSurfaces.All;
        try
        {
            // Keep any production quiet action: an empty registry hid duplicate arming.
            // Other surface stops are unrelated to this contract and may require a desktop.
            PanicSurfaces.All = saved.Select(s => s.Id == "panic-quiet"
                ? s : s with { Stop = _ => { } }).ToArray();
            PanicAftermath.EndQuiet();
            var count = PanicAftermath.PanicCount;
            if (keyAlreadyArmed) PanicAftermath.ArmQuiet();
            PanicSurfaces.StopAll(keyAlreadyArmed ? "panic key" : "tray/voice",
                aftermathArmed: keyAlreadyArmed);
            Assert.Equal(count + 1, PanicAftermath.PanicCount);
            Assert.True(PanicAftermath.TriggersSilenced());
        }
        finally { PanicSurfaces.All = saved; PanicAftermath.EndQuiet(); }
    }
}
