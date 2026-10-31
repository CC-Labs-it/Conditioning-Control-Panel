using System;
using ConditioningControlPanel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using CCP.Avalonia.Testing;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    private static void ChipApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)]
    public void BumpSettleIsWpfsOneOscillationElastic(double t, double expected) =>
        Assert.Equal(expected, new FriendsRailChip.WpfElasticOut().Ease(t), 9);

    [Fact]
    public void BumpSettleOvershootsOnceLikeWpf()
    {
        var ease = new FriendsRailChip.WpfElasticOut();
        var sign = Enumerable.Range(1, 99).Select(i => Math.Sign(ease.Ease(i / 100.0) - 1)).Where(v => v != 0).ToArray();
        var crossings = sign.Zip(sign.Skip(1), (a, b) => a != b).Count(c => c);
        Assert.Equal(2, crossings); // WPF Oscillations=1: rise, one overshoot, one dip; Avalonia's ElasticEaseOut rings ~6 times
    }

    [Fact]
    public Task RailTierAndNameFollowOwnerActivation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        chip.Drawer.MeTier = () => 2;
        chip.Drawer.MeName = () => "First";
        var window = new Window { Content = chip }; window.Show();
        try
        {
            Assert.Equal(13, chip.GetVisualDescendants().OfType<Image>().Single(i => Equals(i.Tag, "friends-tier-2")).Height);
            chip.Drawer.MeTier = () => 0;
            chip.Drawer.MeName = () => "Changed";
            // Headless does not transfer activation. Deliver the native callback through the real subscription.
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.StartsWith("Changed\n", (string)ToolTip.GetTip(chip)!);
            Assert.DoesNotContain(chip.GetVisualDescendants().OfType<Image>(), i => Equals(i.Tag, "friends-tier-2"));
        }
        finally { window.Close(); svc.Dispose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RailHoverSnapsAtOffAndAnimatesOtherwise() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChipApp();
        var oldMotion = CoreSettings.Current.MotionLevel;
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var window = new Window { Content = chip }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            chip.Hover(true);
            var scale = (ScaleTransform)chip.Children.OfType<Border>().Single().RenderTransform!;
            Assert.Equal(1.02, scale.ScaleX);
            chip.Hover(false); Assert.Equal(1, scale.ScaleX);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            chip.Hover(true);
            Assert.NotNull(scale.Transitions);
            Assert.Equal(2, scale.Transitions.Count);
            Assert.All(scale.Transitions, t => Assert.Equal(TimeSpan.FromMilliseconds(150), Assert.IsType<global::Avalonia.Animation.DoubleTransition>(t).Duration));
        }
        finally { window.Close(); svc.Dispose(); CoreSettings.Current.MotionLevel = oldMotion; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RailOnlineCountBumpsAtReducedButNotOffAndCancelsOnDetach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var oldMotion = CoreSettings.Current.MotionLevel;
        var (svc, wire) = Service();
        var chip = new FriendsRailChip(svc);
        var window = new Window { Content = chip }; window.Show();
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            await svc.RefreshAsync();
            Assert.Equal(1, chip.PillCount);
            Assert.Equal(0, chip.ActiveBumps);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            wire.StateBody = State.Replace("\"online\":false", "\"online\":true");
            await svc.RefreshAsync();
            Assert.Equal(2, chip.PillCount);
            Assert.True(chip.ActiveBumps > 0);
            window.Content = null; Assert.Equal(0, chip.ActiveBumps);
        }
        finally { window.Close(); svc.Dispose(); CoreSettings.Current.MotionLevel = oldMotion; }
    });

    [Fact]
    public Task UnreadBadgeBumpsOnlyOnIncreaseAndHonoursMotionOff() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var oldMotion = CoreSettings.Current.MotionLevel;
        var (svc, _) = Service();
        var feed = new FriendsFeed(() => "u_me");
        var chip = new FriendsRailChip(svc);
        chip.Drawer.FeedSource = () => feed;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var window = new Window { Content = chip }; window.Show();
        try
        {
            Assert.Equal(0, chip.ActiveBumps);
            FriendEvent Ev(string key) => new(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, key, Detail: PokeSet.Shipped[0]);
            Assert.True(feed.Add(Ev("first")));
            Assert.Equal(1, chip.UnreadBadge);
            Assert.Equal(1, chip.ActiveBumps);
            window.Content = null;
            Assert.Equal(0, chip.ActiveBumps);
            window.Content = chip;
            chip.UpdateBadge();
            Assert.Equal(0, chip.ActiveBumps); // rebind and equal count must not bump
            feed.MarkAllRead();
            Assert.Equal(0, chip.ActiveBumps); // a decrease must not bump
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Assert.True(feed.Add(Ev("second")));
            Assert.Equal(1, chip.UnreadBadge);
            Assert.Equal(0, chip.ActiveBumps);
            await Task.CompletedTask;
        }
        finally { window.Close(); svc.Dispose(); CoreSettings.Current.MotionLevel = oldMotion; }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public Task ActivationRebindsServiceAndFeedBeforePainting(bool injected) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (first, _) = Service(); var (second, wire) = Service("u_second");
        wire.StateBody = State.Replace("\"online\":false", "\"online\":true");
        await first.RefreshAsync(); await second.RefreshAsync();
        FriendsHead.Service = first;
        var firstFeed = new FriendsFeed(() => "u_first"); var secondFeed = new FriendsFeed(() => "u_second");
        var feed = firstFeed;
        var chip = new FriendsRailChip(injected ? first : null);
        chip.Drawer.FeedSource = () => feed;
        var window = new Window { Content = chip }; window.Show();
        bool Wired(FriendsService svc) => (typeof(FriendsService).GetField("SnapshotChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(svc) as Delegate)?.GetInvocationList().Any(d => ReferenceEquals(d.Target, chip)) == true;
        FriendEvent Ev(string key) => new(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, key, Detail: PokeSet.Shipped[0]);
        try
        {
            chip.Drawer.OnOpened(); chip.Drawer.OnClosed(); // prime its cached old service
            Assert.Equal(1, chip.PillCount);
            FriendsHead.Service = second; feed = secondFeed;
            secondFeed.Add(Ev("replacement"));
            typeof(WindowBase).GetMethod("HandleActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Equal(injected ? 1 : 2, chip.PillCount);
            Assert.Equal(injected, Wired(first)); Assert.Equal(!injected, Wired(second));
            Assert.Equal(1, chip.UnreadBadge);
            firstFeed.Add(Ev("old")); Assert.Equal(1, chip.UnreadBadge);
            secondFeed.Add(Ev("new")); Assert.Equal(2, chip.UnreadBadge);
            window.Content = null; Assert.False(Wired(first)); Assert.False(Wired(second));
        }
        finally { window.Close(); first.Dispose(); second.Dispose(); }
    });

    [Theory]
    [InlineData(MotionLevel.Full, false, false, true)] [InlineData(MotionLevel.Reduced, false, false, true)]
    [InlineData(MotionLevel.Full, true, false, true)] [InlineData(MotionLevel.Reduced, true, false, true)]
    [InlineData(MotionLevel.Full, false, true, true)] [InlineData(MotionLevel.Full, true, true, true)]
    [InlineData(MotionLevel.Full, false, false, false)] [InlineData(MotionLevel.Full, true, false, false)]
    public Task BumpsRenderWpfsRiseSettleAndCancel(MotionLevel level, bool unread, bool gentle, bool osAnimations) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe);
        settings.GentleVisuals = gentle;
        UiMotionPreference.OsProbe = () => osAnimations;
        UiMotionPreference.CacheStamp = long.MinValue;
        var (svc, wire) = Service(); await svc.RefreshAsync();
        var feed = new FriendsFeed(() => "u_clock");
        var chip = new FriendsRailChip(svc); chip.Drawer.FeedSource = () => feed;
        var window = new Window { Content = chip }; window.Show();
        CoreSettings.Current.MotionLevel = level;
        var target = chip.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, unread ? "friends-chip-unread" : "friends-chip-online"));
        var clock = new RailStepClock(); target.SetValue(RailStepClock.Property, clock.Instance);
        double k = level == MotionLevel.Full && !gentle && osAnimations ? 1 : .5;
        void Scale(double expected)
        {
            Assert.Equal(expected, target.RenderTransform!.Value.M11, 5);
            Assert.Equal(expected, target.RenderTransform.Value.M22, 5);
        }
        void Step(double ms) { clock.Step(ms); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        async Task Increase(string key)
        {
            if (unread) feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Mia", DateTime.UtcNow, key, Detail: PokeSet.Shipped[0]));
            else { wire.StateBody = State.Replace("\"online\":false", "\"online\":true"); await svc.RefreshAsync(); }
        }
        try
        {
            await Increase("first");
            Step(0); Step(60); Scale(1 + .225 * k);
            clock.Step(120); Scale(1 + .45 * k); Step(120); Scale(1 + .45 * k);
            Step(270); Scale(1 - .45 * k * (Math.Exp(2) - 1) / (Math.Exp(4) - 1) * Math.Sqrt(.5));
            Step(419); Assert.NotEqual(1, target.RenderTransform!.Value.M11);
            Step(420); Step(421); Scale(1); Assert.Equal(0, chip.ActiveBumps);
            if (!unread) { wire.StateBody = State; await svc.RefreshAsync(); }
            await Increase("second"); Step(500); Step(560); Scale(1 + .225 * k);
            window.Content = null; Step(1000); Scale(1); Assert.Equal(0, chip.ActiveBumps);
            window.Content = chip; Step(1200); Scale(1);
        }
        finally
        {
            window.Close(); svc.Dispose();
            (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe) = old;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    // Avalonia 12 hides IClock. Use its real ClockBase, as SharedDeeperThemeTests does.
    private sealed class RailStepClock
    {
        private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static readonly Type Base = typeof(global::Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
        private static readonly System.Reflection.MethodInfo Pulse = Base.GetMethod("Pulse", Any | System.Reflection.BindingFlags.Instance)!;
        public static readonly AvaloniaProperty Property = (AvaloniaProperty)typeof(global::Avalonia.Animation.Animatable).GetField("ClockProperty", Any | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
        public void Step(double ms) => Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
    }
}
