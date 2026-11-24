using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF SubliminalService.cs:311 FlashSubliminalCustom fadeInMs/fadeOutMs (the Back Room word envelope)
/// and :138 the EMI Desk moment when the schedule starts.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalCardFadeTests
{
    private static readonly PixelRect Bounds = new(0, 0, 300, 200);

    private static void WithStage(Action<Func<Grid>, List<(TimeSpan At, Action Fire)>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var settingsProvider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = null;   // Gentle Visuals off: the authored fades as they are
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try { body(() => Assert.IsType<Grid>(stage!.Content), new List<(TimeSpan, Action)>()); }
        finally
        {
            SubliminalOverlay.CloseAll();
            SharedOverlayHost.Configure = configure;
            CoreSettings.ServiceProvider = settingsProvider;
        }
    });

    private static void Advance(List<(TimeSpan At, Action Fire)> scheduled, TimeSpan at)
    {
        foreach (var item in scheduled.Where(item => item.At <= at).OrderBy(item => item.At).ToArray())
        {
            scheduled.Remove(item);
            item.Fire();
        }
    }

    [Fact]
    public void Hosted_card_uses_its_own_fade_in_and_fade_out() => WithStage((canvas, scheduled) =>
    {
        var fadeIn = SubliminalOverlayWindow.Fade(BackRoomFxPlan.WordFadeInMs);
        var fadeOut = SubliminalOverlayWindow.Fade(BackRoomFxPlan.WordFadeOutMs);
        Assert.Equal((80, 350), ((int)fadeIn.TotalMilliseconds, (int)fadeOut.TotalMilliseconds));
        var hold = TimeSpan.FromMilliseconds(BackRoomFxPlan.WordHoldMs);
        Assert.True(SubliminalOverlay.ShowHosted(Bounds, "WORD", 0.8, hold, (fire, at) => scheduled.Add((at, fire)), fadeIn, fadeOut));
        var card = Assert.Single(canvas().Children);
        Advance(scheduled, TimeSpan.FromMilliseconds(50));     // the default fade would be complete here
        Assert.True(card.Opacity < 0.8, $"fade in finished early at {card.Opacity}");
        Advance(scheduled, fadeIn);
        Assert.Equal(0.8, card.Opacity, 3);
        Advance(scheduled, fadeIn + hold + TimeSpan.FromMilliseconds(100));   // the default card would be gone
        Assert.Same(card, Assert.Single(canvas().Children));
        Assert.InRange(card.Opacity, 0.01, 0.79);
        Advance(scheduled, fadeIn + hold + fadeOut);
        Assert.Empty(canvas().Children);
    });

    [Fact]
    public void Panic_removes_a_card_mid_fade_and_its_pending_steps_stay_dead() => WithStage((canvas, scheduled) =>
    {
        var hook = CoreEngine.StoppedHook;
        // The shell's OnEngineStopped (MainShellWindow.StartStop.cs:200) on the engine panic row.
        CoreEngine.StoppedHook = () => ConditioningControlPanel.Avalonia.App.StopDesktopOverlays(final: false);
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "WORD", 0.8, TimeSpan.FromMilliseconds(400),
                (fire, at) => scheduled.Add((at, fire)), SubliminalOverlayWindow.Fade(80), SubliminalOverlayWindow.Fade(350)));
            var card = Assert.Single(canvas().Children);
            Advance(scheduled, TimeSpan.FromMilliseconds(40));
            PanicSurfaces.All.Single(s => s.Id == "engine").Stop(null);
            Assert.Empty(canvas().Children);
            var opacity = card.Opacity;
            Advance(scheduled, TimeSpan.FromSeconds(5));
            Assert.Equal(opacity, card.Opacity);
            Assert.Empty(canvas().Children);
        }
        finally { CoreEngine.StoppedHook = hook; }
    });

    [Fact]
    public void Custom_passes_fades_to_the_card_and_plain_customs_keep_their_path() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = (SubliminalWhisperShow.DrawWithFades, SubliminalWhisperShow.DrawWithDuration, CoreProgression.AddXPProvider);
        var log = new List<string>();
        SubliminalWhisperShow.DrawWithFades = (t, o, d, i, f) => log.Add($"fades {t} {o} {d} {i} {f}");
        SubliminalWhisperShow.DrawWithDuration = (t, o, d) => log.Add($"duration {t} {o} {d}");
        CoreProgression.AddXPProvider = (_, _) => { };
        try
        {
            SubliminalWhisperShow.Custom("WORD", 60, BackRoomFxPlan.WordHoldMs, true, BackRoomFxPlan.WordFadeInMs, BackRoomFxPlan.WordFadeOutMs);
            SubliminalWhisperShow.Custom("PLAIN", 60, 400, true);
            Assert.Equal(new[] { "fades WORD 60 400 80 350", "duration PLAIN 60 400" }, log);
        }
        finally { (SubliminalWhisperShow.DrawWithFades, SubliminalWhisperShow.DrawWithDuration, CoreProgression.AddXPProvider) = old; }
    });

    [Fact]
    public void Starting_the_schedule_fires_the_emi_moment_once_and_stopping_does_not() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = (SubliminalWhisperShow.EmiMoment, CoreSubliminal.RunStateChanged, CoreSettings.Current.SubliminalEnabled);
        var moments = new List<string>();
        SubliminalWhisperShow.EmiMoment = moments.Add;
        CoreSubliminal.RunStateChanged = SubliminalWhisperShow.RunStateChanged;   // as App.axaml.cs seeds it
        CoreSettings.Current.SubliminalEnabled = false;   // no tick is armed
        try
        {
            CoreSubliminal.Start();
            Assert.Equal(new[] { "subliminalsStarted" }, moments);
            CoreSubliminal.Start();   // idempotent: no second moment
            CoreSubliminal.Stop();
            Assert.Equal(new[] { "subliminalsStarted" }, moments);
        }
        finally
        {
            CoreSubliminal.Stop();
            (SubliminalWhisperShow.EmiMoment, CoreSubliminal.RunStateChanged, CoreSettings.Current.SubliminalEnabled) = old;
        }
    });
}
