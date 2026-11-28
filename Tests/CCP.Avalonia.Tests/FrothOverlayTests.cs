using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane L3 of the Linux Super port: Froth on the ambient bubbles (WPF BubbleService.Froth.cs, 7.4.1):
/// the free-account gate, the sum, the shared stage (blast, Whirlpool pull and eye, sweep) under
/// <see cref="SteamOverlay.Gate"/>, the cursor push, panic and the --render-all sample.</summary>
public sealed class FrothOverlayTests
{
    private static void Run(Action<Window, AppSettings> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SteamOverlay.Seed();   // seed first, so the restore below hands back the seeded cursor (SuperPointerTests)
        var host = new Window();
        var (provider, checks, effect, cursor) = (CoreSettings.ServiceProvider, BubbleOverlay.SkipPlatformChecksForTests,
            BubbleOverlay.FrothEffectOn, SuperStageHost.CursorProvider);
        var service = new SettingsService();
        service.Current.BubblesEnabled = true;
        service.Current.FrothCursorPush = 0;   // no real cursor reaches a test unless it asks
        try
        {
            CoreSettings.ServiceProvider = () => service;
            SuperStageHost.CursorProvider = () => null;
            BubbleOverlay.SkipPlatformChecksForTests = true;
            BubbleOverlay.FrothEffectOn = _ => true;
            host.Show();
            body(host, service.Current);
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Stop();
            SuperStageHost.Stage.PoolOn = false; SuperStageHost.Stage.Pool.Reset(); SuperStageHost.Stage.MovingPops = false;
            SuperStageHost.Stage.Pool.Eats = true;
            (CoreSettings.ServiceProvider, BubbleOverlay.SkipPlatformChecksForTests, BubbleOverlay.FrothEffectOn, SuperStageHost.CursorProvider)
                = (provider, checks, effect, cursor);
            host.Close();
        }
    });

    private static AmbientBubble Plain(double x, double y, double size = 150) => new() { X = x, Y = y, Size = size, Clickable = true };

    private static void Begin(Window host, params AmbientBubble[] bubbles)
    {
        BubbleOverlay.Start(host);
        Assert.True(BubbleOverlay.IsRunning);
        BubbleOverlay.Field.Bubbles.Clear();
        BubbleOverlay.Field.Bubbles.AddRange(bubbles);
    }

    private static double _t = 10;
    private static void Frame() => BubbleOverlay.OnFrame(TimeSpan.FromSeconds(_t += 0.04));

    [Fact]
    public void A_free_account_never_froths_without_the_debug_override() => Run((host, s) =>
    {
        var (premium, trying) = (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider);
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.InnerBloom, SuperMode.Both);
            SuperAccess.TryingProvider = () => null;
            CoreEntitlement.HasPremiumProvider = () => false;
            BubbleOverlay.FrothEffectOn = SuperAccess.IsOn;   // the shipped switch
            Begin(host, Plain(300, 300), Plain(300, 300));
            Frame();
            Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count);
            CoreEntitlement.HasPremiumProvider = () => true;
            SuperAccess.RaiseChanged(SuperEffect.InnerBloom);
            Frame();
            Assert.Equal(2, Assert.Single(BubbleOverlay.Field.Bubbles).FrothPartCount);
            Assert.Contains(SuperEffect.InnerBloom, global::ConditioningControlPanel.Avalonia.Controls.Super.SuperBox.HostedOnThisHead);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider) = (premium, trying);
        }
    });

    [Fact]
    public void A_merged_bubble_pays_the_sum_of_its_parts_and_never_a_bonus() => Run((host, s) =>
    {
        int pops = 0;
        Action counted = () => pops++;
        BubbleOverlay.Popped += counted;
        try
        {
            Begin(host, Plain(300, 300), Plain(300, 300), Plain(300, 300));
            Frame();
            var merged = Assert.Single(BubbleOverlay.Field.Bubbles);
            Assert.Equal(3, merged.FrothPartCount);
            int before = AmbientBubbleXp.PaidToday(s);
            Assert.True(before <= AmbientBubbleXp.DailyXpCap - 15);
            BubbleOverlay.Pop(merged);
            Assert.Equal(15, AmbientBubbleXp.PaidToday(s) - before);   // 3 x 5 XP, the size pays nothing
            Assert.Equal(3, pops);                                       // three pops counted: 3 of the 100 for one Sparkle
        }
        finally { BubbleOverlay.Popped -= counted; }
    });

    [Fact]
    public void Every_blast_holds_the_steam_gate_and_a_card_break_sets_froth_off() => Run((host, s) =>
    {
        var held = new System.Collections.Generic.List<bool>();
        Action<Blast> probe = _ => held.Add(Monitor.IsEntered(SteamOverlay.Gate));
        SuperStageHost.Stage.Blasted += probe;
        try
        {
            var near = Plain(600, 300);
            Begin(host, Plain(100, 100, 400), near);
            BubbleOverlay.Pop(BubbleOverlay.Field.Bubbles[0]);   // a big pop reaches its neighbours
            Assert.Equal(new[] { true }, held);
            Assert.True(near.FrothFuse >= 0);
            for (int i = 0; i < 30 && !near.Popping; i++) Frame();
            Assert.True(near.Popping);                           // the ring arrived: it went off and blasted in turn
            Assert.Equal(2, held.Count);
            Assert.All(held, Assert.True);

            var other = Plain(300, 300);
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.Add(other);
            var c = BubbleOverlay.CenterPx(other);
            lock (SteamOverlay.Gate) SuperStageHost.Stage.Blast(new Blast(c.X + 100, c.Y, 400, 0, BlastKind.CardBreak));
            Assert.True(other.FrothFuse >= 0);
        }
        finally { SuperStageHost.Stage.Blasted -= probe; }
    });

    [Fact]
    public void Pop_reach_zero_never_sets_a_neighbour_off() => Run((host, s) =>
    {
        s.FrothPopReach = 0;
        var near = Plain(600, 300);
        Begin(host, Plain(100, 100, 400), near);
        BubbleOverlay.Pop(BubbleOverlay.Field.Bubbles[0]);
        for (int i = 0; i < 20; i++) Frame();
        Assert.False(near.Popping);
        Assert.True(near.FrothFuse < 0);
    });

    [Theory]
    [InlineData(true, false, true)]    // Moving effects pop on: the eye takes it, and it pays like a pop
    [InlineData(true, true, false)]    // Stay centred: the pool pulls and never eats
    [InlineData(false, false, false)]  // Moving effects pop off (shipped default): never eaten
    public void The_whirlpool_eye_takes_a_froth_bubble_only_when_allowed(bool movingPops, bool centred, bool eaten) => Run((host, s) =>
    {
        var b = Plain(300, 300);
        Begin(host, b);
        Frame();
        var c = BubbleOverlay.CenterPx(b);
        var stage = SuperStageHost.Stage;
        stage.PoolOn = true;
        stage.MovingPops = movingPops;
        stage.Pool.Eats = !centred;
        stage.Pool.Step(10, c.X, c.Y, 0, 1, 1080);   // a resting cursor grew the pool right on the bubble
        Assert.True(stage.PoolLive);
        int before = AmbientBubbleXp.PaidToday(s);
        Frame();
        Assert.Equal(eaten, b.Popping);
        Assert.Equal(eaten ? 5 : 0, AmbientBubbleXp.PaidToday(s) - before);   // WPF :152: the pool's bubble pays normally
        if (!eaten) Assert.True(b.FrothScale < 0.5);                            // drawn small at the eye
    });

    [Fact]
    public void The_cursor_pushes_a_bubble_away_and_push_zero_leaves_it() => Run((host, s) =>
    {
        var near = Plain(0, 100, 100);
        var far = Plain(0, 800, 100);
        Begin(host, near, far);
        SuperStageHost.CursorProvider = () =>
        {
            var c = BubbleOverlay.CenterPx(near);
            return ((int)(c.X + (near.FrothRadius + 20) * BubbleOverlay.ScaleFor(near)), (int)c.Y);
        };
        s.FrothCursorPush = 0;
        for (int i = 0; i < 12; i++) Frame();
        Assert.Equal(far.X, near.X, 6);                                   // Cursor push 0: the cursor never moves a bubble
        s.FrothCursorPush = 100;
        for (int i = 0; i < 12; i++) Frame();
        Assert.True(near.X < far.X - 1, $"near {near.X} far {far.X}");   // same wobble, only the push differs
    });

    [Fact]
    public void A_sweep_pops_the_froth_under_the_word_only_with_moving_effects_pop_on() => Run((host, s) =>
    {
        var b = Plain(300, 300);
        Begin(host, b);
        Frame();
        var c = BubbleOverlay.CenterPx(b);
        SuperStageHost.Stage.MovingPops = false;
        SuperStageHost.Stage.Sweep(c.X - 10, c.Y - 10, c.X + 10, c.Y + 10);
        Assert.False(b.Popping);
        SuperStageHost.Stage.MovingPops = true;
        SuperStageHost.Stage.Sweep(c.X - 10, c.Y - 10, c.X + 10, c.Y + 10);
        Assert.True(b.Popping);
    });

    [Fact]
    public void Panic_ends_froth_at_once_and_a_later_blast_reaches_nothing() => Run((host, s) =>
    {
        Begin(host, Plain(300, 300), Plain(300, 300));
        Frame();
        Assert.Equal(2, Assert.Single(BubbleOverlay.Field.Bubbles).FrothPartCount);
        foreach (var id in new[] { "super-stage", "bubble-host" })
            PanicSurfaces.All.Single(x => x.Id == id).Stop(null);
        Assert.False(BubbleOverlay.IsRunning);
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        var late = Plain(300, 300);
        BubbleOverlay.Field.Bubbles.Add(late);
        lock (SteamOverlay.Gate) SuperStageHost.Stage.Blast(new Blast(0, 0, 5000, 0, BlastKind.CardBreak));
        Assert.True(late.FrothFuse < 0);
        BubbleOverlay.Field.Bubbles.Clear();
    });

    [Fact]
    public void Render_all_draws_a_ripe_merged_sample() => Run((host, s) =>
    {
        BubbleOverlay.Field.Bubbles.Clear();
        var w = new BubbleOverlayWindow();
        try
        {
            var merged = Assert.Single(BubbleOverlay.Field.Bubbles, b => b.FrothPartCount == 2);
            Assert.True(merged.FrothRim > 0.99);
            Assert.True(merged.FrothScale > 1.3);
        }
        finally
        {
            BubbleOverlay.Field.StepFroth(false, 0, 45, 4.5, _ => { });
            BubbleOverlay.Field.Bubbles.Clear();
            w.Close();
        }
    });
}
