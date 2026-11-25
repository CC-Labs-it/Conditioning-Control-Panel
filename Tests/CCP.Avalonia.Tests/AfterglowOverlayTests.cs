using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Super Afterglow on this head: every pool subliminal leaves a ghost in a line along the
/// bottom of one monitor, drawn on a passive subliminal stage. The gate is Core's SuperAccess, tested
/// WITHOUT the DEBUG override.</summary>
public class AfterglowOverlayTests
{
    private static global::Avalonia.Platform.Screen Screen()
    {
        var s = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(s, 1.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(s, new PixelRect(0, 0, 800, 600));
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(s, true);
        return s;
    }

    /// <summary>A test profile with subliminals on, a stage that always configures and one screen.
    /// <paramref name="premium"/> is the real tier answer; the Afterglow star is switched on.</summary>
    private static Task With(bool premium, Action<ConditioningControlPanel.Models.AppSettings> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var saved = (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, SharedOverlayHost.Configure, ScreenList.QueryOverride,
            AfterglowLineOverlay.SurfaceAvailable, AfterglowLineOverlay.HostProvider, Environment.GetEnvironmentVariable("CCP_SUPER_ALL"));
        var host = new Window();
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);   // the gate itself, never the DEBUG override
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => premium;
            SharedOverlayHost.Configure = (_, _) => true;
            ScreenList.QueryOverride = _ => new[] { Screen() };
            AfterglowLineOverlay.SurfaceAvailable = () => true;
            AfterglowLineOverlay.HostProvider = () => host;
            var s = settings.Current;
            s.SubliminalEnabled = true;
            s.GentleVisuals = false;
            SuperAccess.Set(SuperEffect.Afterglow, true);
            host.Show();
            body(s);
        }
        finally
        {
            AfterglowLineOverlay.StopAll();
            (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, SharedOverlayHost.Configure, ScreenList.QueryOverride,
                AfterglowLineOverlay.SurfaceAvailable, AfterglowLineOverlay.HostProvider) = (saved.Item1, saved.Item2, saved.Item3, saved.Item4, saved.Item5);
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", saved.Item6);
            host.Close();
            CoreSettings.ServiceProvider = provider;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AFreeAccountGetsNoGhostAndKeepsItsCards() => With(premium: false, _ =>
    {
        AfterglowLineOverlay.WordShown("good");
        Assert.False(AfterglowLineOverlay.Up);
        Assert.Equal(0, AfterglowLineOverlay.Count);
        Assert.False(AfterglowLineOverlay.RestsCard());
        Assert.Empty(AfterglowLineOverlay.ActiveTextRects());
    });

    [Fact]
    public Task TheLineFillsLightsTellsTheFogAndClears() => With(premium: true, s =>
    {
        Assert.True(AfterglowLineOverlay.RestsCard());   // Afterglow alone: the ambient card rests
        s.AfterglowAlsoBase = true;
        Assert.False(AfterglowLineOverlay.RestsCard());  // "Also show the normal subliminals"
        s.AfterglowLineWords = 2;

        AfterglowLineOverlay.WordShown("Deeper");
        Assert.True(AfterglowLineOverlay.Up);
        var band = Assert.Single(AfterglowLineOverlay.ActiveTextRects());
        Assert.True(band.Top > 300, "the line's band sits at the bottom of the monitor");
        AfterglowLineOverlay.WordShown("softer");
        Assert.Equal(2, AfterglowLineOverlay.Count);

        // The picture is Core's; here it only has to draw the monitor's line without throwing.
        var canvas = Assert.IsType<AfterglowLineCanvas>(AfterglowLineOverlay.Canvas);
        using (var surface = SKSurface.Create(new SKImageInfo(800, 600)))
        {
            AfterglowLineOverlay.Step(0.3);
            canvas.Draw(surface.Canvas);
            AfterglowLineOverlay.Step(AfterglowLine.LandSeconds);   // both landed: the finale starts
            Assert.Equal("deeper softer", AfterglowLineOverlay.FogLine);
            canvas.Draw(surface.Canvas);
        }

        AfterglowLineOverlay.Step(AfterglowLine.FinaleRise + AfterglowLineOptions.HoldMax + AfterglowLine.FinaleSink);
        Assert.Equal(0, AfterglowLineOverlay.Count);
        Assert.False(AfterglowLineOverlay.Up);
        Assert.Empty(AfterglowLineOverlay.ActiveTextRects());
    });

    [Fact]
    public Task StopAndTheStarGoingOffTakeTheLineDownAtOnce() => With(premium: true, _ =>
    {
        AfterglowLineOverlay.WordShown("good");
        Assert.True(AfterglowLineOverlay.Up);
        // Panic, Stop everything and the subliminal service stopping all reach CloseAll.
        SubliminalOverlay.CloseAll();
        Assert.False(AfterglowLineOverlay.Up);
        Assert.Equal(0, AfterglowLineOverlay.Count);

        AfterglowLineOverlay.WordShown("good");
        Assert.True(AfterglowLineOverlay.Up);
        SuperAccess.Set(SuperEffect.Afterglow, false);
        Assert.False(AfterglowLineOverlay.Up);
        AfterglowLineOverlay.WordShown("good");
        Assert.False(AfterglowLineOverlay.Up);
    });

    [Fact]
    public Task ARetiredOneShotLeavesTheLineAlone() => With(premium: true, _ =>
    {
        AfterglowLineOverlay.WordShown("good");
        SubliminalOverlay.CloseOwned(new object());
        SubliminalWhisperShow.StopOneShots();
        Assert.True(AfterglowLineOverlay.Up);
    });

    [Fact]
    public Task ARestedCardStillCountsAndLeavesItsGhost() => With(premium: true, _ =>
    {
        var (draw, rested) = (SubliminalWhisperShow.Draw, SubliminalWhisperShow.DrawRested);
        var log = new List<string>();
        try
        {
            SubliminalWhisperShow.Draw = t => log.Add("card " + t);
            SubliminalWhisperShow.DrawRested = t => log.Add("rested " + t);
            SubliminalWhisperShow.Phrase("no clip for this one");
            Assert.Equal(new[] { "rested no clip for this one" }, log);
            Assert.Equal(1, AfterglowLineOverlay.Count);
        }
        finally { (SubliminalWhisperShow.Draw, SubliminalWhisperShow.DrawRested) = (draw, rested); }
    });

    [Fact]
    public void TheRenderSampleDrawsALitLine()
    {
        var canvas = new AfterglowLineCanvas();
        using var surface = SKSurface.Create(new SKImageInfo(1100, 780));
        canvas.Draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();
        int lit = 0;
        for (int y = 600; y < 780; y += 4)
            for (int x = 0; x < 1100; x += 4)
                if (pixels.GetPixelColor(x, y).Alpha > 0) lit++;
        Assert.True(lit > 0, "the sample line paints near the bottom");
    }
}
