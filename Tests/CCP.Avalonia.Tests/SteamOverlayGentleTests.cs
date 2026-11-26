using System;
using System.Diagnostics;
using System.Reflection;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>L8 owed items: the fog rises under Gentle visuals (WPF SteamLayer GentleFades), and the
/// bouncing word / Scrawl stamp face falls back to a bundled face Linux has.</summary>
public sealed class SteamOverlayGentleTests
{
    private static void Tick()
        => typeof(SteamOverlay).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { null, EventArgs.Empty });

    private static void Rewind(double seconds)
    {
        var f = typeof(SteamOverlay).GetField("_last", BindingFlags.Static | BindingFlags.NonPublic)!;
        f.SetValue(null, Stopwatch.GetTimestamp() - (long)(seconds * Stopwatch.Frequency));
    }

    [Fact]
    public void AFreshFogRisesUnderGentleVisualsAndCutsWithout() => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            SteamOverlay.Seed();
            settings.Current.GentleVisuals = true;
            settings.Current.GentleFadeSeconds = 1.0;
            SuperStageHost.SteamSurface(true);
            Assert.Equal(0, SteamOverlay.ShowAlpha);
            Tick();
            Rewind(0.1);                                        // one tick is capped at 0.1 s
            Tick();
            Assert.InRange(SteamOverlay.ShowAlpha, 0.05, 0.2);  // about a tenth of a 1 s fade
            for (int i = 0; i < 12; i++) { Rewind(0.1); Tick(); }
            Assert.Equal(1, SteamOverlay.ShowAlpha);

            SuperStageHost.SteamSurface(false);
            settings.Current.GentleVisuals = false;
            SuperStageHost.SteamSurface(true);
            Assert.Equal(1, SteamOverlay.ShowAlpha);            // Gentle off: no fade, as before
        }
        finally
        {
            SuperStageHost.Stop();
            CoreSettings.ServiceProvider = provider;
            settings.SealForReset();
        }
    });

    [Fact]
    public void TheWordFaceEndsInABundledFace() => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.BouncingTextFont = "";
            Assert.Equal("CCP Sans", BouncingTextOverlay.Family().FamilyNames[^1]);
            settings.Current.BouncingTextFont = "Comic Sans MS";
            var names = BouncingTextOverlay.Family().FamilyNames;
            Assert.Equal("Comic Sans MS", names[0]);
            Assert.Equal("CCP Sans", names[^1]);
        }
        finally
        {
            CoreSettings.ServiceProvider = provider;
            settings.SealForReset();
        }
    });
}
