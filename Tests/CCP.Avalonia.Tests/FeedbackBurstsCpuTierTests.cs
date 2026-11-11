using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Controls.Header;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The CPU path's lighter tier (WPF's Performance tier) with the user's own settings at defaults, or the
/// user's own Performance mode on top. Owner decision 1b (fix3 pop, 2026-11-06): bursts the user triggers keep
/// playing on the CPU path; the user's own Performance mode, Reduced/Off motion and Gentle visuals still drop them.</summary>
internal static class CpuTier
{
    /// <param name="userPerformance">The user's own Settings ▸ Performance mode (WPF drops the bursts there).</param>
    internal static void On(bool userPerformance = false, MotionLevel motion = MotionLevel.Full)
    {
        var s = CoreSettings.Current;
        (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode, s.GentleVisuals, s.LighterEffects) =
            (motion, userPerformance, false, false, LighterEffectsMode.Auto);
        SoftwareRender.ResetForTests();
        SoftwareRender.Report(true);   // the probe found CPU Skia (WSLg, a VM)
        Assert.True(SoftwareRender.FloorActive);
        Assert.False(AmbientFxCanvas.Env.AllowParticles);   // ambient particles: off on the CPU path, as before
    }

    internal static void Off() => SoftwareRender.ResetForTests();
}

/// <summary>One test per burst family moved to <see cref="AmbientFxCanvas.Env.AllowFeedbackParticles"/>: on the CPU
/// path the burst is present; under the user's own Performance mode it is absent (as WPF). The billboard press and
/// the launcher play burst are pinned in BillboardMotionTests and LauncherFxTests.</summary>
public sealed class FeedbackBurstsCpuTierTests
{
    private static void Platform()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try { body(); }
        finally { CpuTier.Off(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    /// <summary>AmbientFxCanvas.Burst: the canvas behind the nav pill sparks, the vault card click, the favourites
    /// drawer, the launcher bursts and every EventBurstLayer moment. On the CPU path its ambient clock never runs,
    /// so the burst steps at the user's tier's rate (30 fps at Quality), not the 1 s idle interval.</summary>
    [Theory]
    [InlineData(false, MotionLevel.Full, true)]
    [InlineData(true, MotionLevel.Full, false)]
    [InlineData(false, MotionLevel.Reduced, false)]
    public Task CanvasBurst(bool userPerformance, MotionLevel motion, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance, motion);
        var canvas = new AmbientFxCanvas { Width = 300, Height = 200 };
        var w = new Window { Width = 300, Height = 200, Content = canvas };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(present, AmbientFxCanvas.Env.AllowFeedbackParticles);
            canvas.Burst(150, 100, null, 90);
            if (!present) { Assert.Equal(0, canvas.BurstCount); return; }
            Assert.Equal(90, canvas.BurstCount);
            Assert.True(canvas.IsRunning);
            Assert.Equal(1000.0 / 30, canvas.StepInterval.TotalMilliseconds, 1);
        }
        finally { w.Close(); }
    });

    /// <summary>EventBurstLayer.Fire: achievement unlock, level-up, tier-raise, quest and program completion.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task EventBurst(bool userPerformance, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance);
        var anchor = new Border { Width = 80, Height = 40 };
        var w = new Window { Width = 400, Height = 300, Content = new Panel { Children = { anchor } } };
        var layer = new EventBurstLayer();
        try
        {
            w.Show(); w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(present, layer.Fire(anchor, 60));
            Assert.Equal(present ? 1 : 0, layer.Count);
        }
        finally { layer.Stop(); w.Close(); }
    });

    /// <summary>The friends poke's floating word throws its sparks.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task FloatingWordSparks(bool userPerformance, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance);
        var owner = new Window { Width = 600, Height = 400 };
        try
        {
            owner.Show();
            Dispatcher.UIThread.RunJobs();
            var word = FloatingWord.Throw(owner, "poke", pink: true)!;
            Assert.Equal(present, word.SparkDots > 0);
            word.Close();
        }
        finally { owner.Close(); }
    });

    /// <summary>The Sparkle wallet's reward: the rise and its particles.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task WalletReward(bool userPerformance, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance);
        var wallet = new SparkleWallet();
        var w = new Window { Width = 400, Height = 200, Content = wallet };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            wallet.ShowReward(5);
            Assert.True(wallet.RewardShowing);
            Assert.Equal(present, wallet.RewardParticles);
        }
        finally { w.Close(); }
    });

    /// <summary>The header premium spark's click burst. WPF gates it on the motion level only (MotionFrom folds the
    /// Performance tier into Reduced, which still bursts), so it already played on the CPU path and under the user's
    /// Performance mode; Off motion is the one setting that stills it.</summary>
    [Theory]
    [InlineData(false, MotionLevel.Full, true)]
    [InlineData(true, MotionLevel.Full, true)]
    [InlineData(false, MotionLevel.Off, false)]
    public Task PremiumSparkClick(bool userPerformance, MotionLevel motion, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance, motion);
        var (premium, lab) = (CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider);
        CoreAccount.HasPremiumAccessProvider = () => true;
        CoreAccount.HasLabAccessProvider = () => false;
        var spark = new PremiumSpark();
        var w = new Window { Content = spark };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            spark.OnClick();
            Assert.Equal(present, spark.Field.Alive.Any(m => m.Kind == MoteKind.Burst));
        }
        finally { w.Close(); (CoreAccount.HasPremiumAccessProvider, CoreAccount.HasLabAccessProvider) = (premium, lab); }
    });

    /// <summary>A click on a vault card bursts (and holds the door for it), and the favourites drawer's handle
    /// bursts though its ambient canvas never starts on the CPU path.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task VaultClickAndFavouritesDrawer(bool userPerformance, bool present) => Run(() =>
    {
        CpuTier.On(userPerformance);
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var card = view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "ExCard");
            var feature = ((ExclusiveCardRow)card.DataContext!).Feature;
            var canvas = view.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!;
            int opened = 0;
            view.OnVaultCardClicked(card, feature, () => opened++);
            Assert.Equal(present, canvas.BurstCount > 0);
            Assert.Equal(present ? 0 : 1, opened);   // a burst holds the door for its beat, as WPF

            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            var settings = shell.Named<SettingsTabView>("SettingsTab")!;
            var drawer = settings.FindControl<AmbientFxCanvas>("FavoritesDrawerFx")!;
            Assert.False(drawer.IsRunning);   // no ambient clock on the CPU path
            settings.BurstFavoritesDrawer(90);
            Assert.Equal(present, drawer.BurstCount > 0);
        }
        finally { shell.Close(); }
    });
}
