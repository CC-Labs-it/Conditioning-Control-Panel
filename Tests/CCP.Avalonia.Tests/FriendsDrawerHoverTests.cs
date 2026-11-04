using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsDrawerHoverTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Border Row(Control root, string tag) => root.GetLogicalDescendants().OfType<Border>()
        .Single(c => c.Tag as string == tag);

    private static void Pointer(Border row, bool enter) => row.RaiseEvent(new PointerEventArgs(
        enter ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent,
        row, null!, row, default, 0, PointerPointProperties.None, KeyModifiers.None));

    private static void AssertWash(Border row) =>
        Assert.Equal(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(row.Background).Color);

    // WPF FriendsDrawer.cs:514-526,588,608-609; FriendsDrawer.Juice.cs:93-102.
    [Fact]
    public Task FriendHoverRestoresTheClosedOrHostingBrushAndKeepsAnOpenCardRaised() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var row = Row(drawer, "friends-row:u_on");
        var rest = row.Background;
        Pointer(row, true);
        AssertWash(row);
        Pointer(row, false);
        Assert.Same(rest, row.Background);

        drawer.CanJoinTables = () => true;
        drawer.Tables = () => GoonOpenTablesApi.Parse("""{"ok":true,"tables":[{"code":"MIA123","name":"Mia","friend":true,"friendId":"u_on","cardSec":60}]}""");
        drawer.Render();
        row = Row(drawer, "friends-row:u_on");
        rest = Assert.IsType<LinearGradientBrush>(row.Background);
        Pointer(row, true);
        AssertWash(row);
        Pointer(row, false);
        Assert.Same(rest, row.Background);

        drawer.Toggle("u_on");
        row = Row(drawer, "friends-row:u_on");
        rest = row.Background;
        Pointer(row, true);
        Assert.Same(rest, row.Background);
        Pointer(row, false);
        Assert.Same(rest, row.Background);
    });

    // A pointer reaches the request row through the real window hit test.
    [Fact]
    public Task PointerInTheRequestMarginShowsAndClearsTheWash() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var window = new Window { Width = 420, Height = 650, Content = drawer };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = Row(drawer, "friends-request-in:u_req");
            var rest = row.Background;
            window.MouseMove(new Point(2, 2));
            window.MouseMove(row.TranslatePoint(new Point(3, 3), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            AssertWash(row);
            window.MouseMove(new Point(2, 2));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(rest, row.Background);
        }
        finally { window.Close(); }
    });

    // WPF FriendsDrawer.cs:745-753,857-858; FriendsDrawer.Juice.cs:93-102.
    [Fact]
    public Task RequestRowsHaveAHitTestableRestingBrushAndRestoreItAfterHover() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (svc, wire) = FriendsDrawerTests.Service();
        using (svc)
        {
            wire.StateBody = wire.StateBody.Replace("\"outgoing\":[]", "\"outgoing\":[{\"id\":\"u_out\",\"name\":\"Cat\",\"at\":\"2026-09-01T00:00:00Z\"}]");
            await svc.RefreshAsync();
            var drawer = new FriendsDrawer(svc);
            foreach (var tag in new[] { "friends-request-in:u_req", "friends-request-out:u_out" })
            {
                var row = Row(drawer, tag);
                var rest = Assert.IsAssignableFrom<ISolidColorBrush>(row.Background);
                Assert.Equal(Colors.Transparent, rest.Color);
                Pointer(row, true);
                AssertWash(row);
                Pointer(row, false);
                Assert.Same(rest, row.Background);
            }
        }
    });
    // WPF FriendsDrawer.Juice.cs:93-134: every row sweeps once, even when open; Off stays still.
    [Fact]
    public Task SheenCoversFriendAndRequestRowsAndHonorsMotion() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var previous = settings.MotionLevel;
        var gentle = settings.GentleVisuals;
        var probe = UiMotionPreference.OsProbe;
        settings.GentleVisuals = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var window = new Window { Width = 420, Height = 650, Content = drawer };
        try
        {
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var tag in new[] { "friends-row:u_on", "friends-request-in:u_req" })
            {
                var row = Row(drawer, tag);
                Pointer(row, true);
                var band = row.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                    .Single(x => x.Tag as string == "friends-sheen");
                Assert.False(band.IsHitTestVisible);
                Assert.Equal(70, band.Width);
                Assert.Equal(0.9, band.Opacity);
                await FriendsDrawerTests.Until(() => !row.GetLogicalDescendants().Contains(band));
                Assert.DoesNotContain(band, row.GetLogicalDescendants());
            }
            drawer.Toggle("u_on");
            var open = Row(drawer, "friends-row:u_on");
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Reduced;
            Pointer(open, true);
            var reduced = open.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                .Single(x => x.Tag as string == "friends-sheen");
            Assert.Equal(0.45, reduced.Opacity);
            await FriendsDrawerTests.Until(() => !open.GetLogicalDescendants().Contains(reduced));
            Assert.DoesNotContain(reduced, open.GetLogicalDescendants());
            ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            Pointer(open, true);
            Assert.DoesNotContain(open.GetLogicalDescendants(), x => x is Control c && c.Tag as string == "friends-sheen");
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            settings.GentleVisuals = true;
            Pointer(open, true);
            var gentleBand = open.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                .Single(x => x.Tag as string == "friends-sheen");
            Assert.Equal(0.45, gentleBand.Opacity);
        }
        finally
        {
            window.Close(); settings.MotionLevel = previous; settings.GentleVisuals = gentle;
            UiMotionPreference.OsProbe = probe; UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    // Avalonia 12 keeps IClock internal. As in SharedDeeperThemeTests, pulse its clock explicitly.
    private sealed class StepClock
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type Base = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
        private static readonly MethodInfo Pulse = Base.GetMethod("Pulse", Any | BindingFlags.Instance)!;
        public static readonly AvaloniaProperty Property = (AvaloniaProperty)typeof(Animatable)
            .GetField("ClockProperty", Any | BindingFlags.Static)!.GetValue(null)!;
        public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
        public void Step(double ms) => Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
    }

    // WPF FriendsDrawer.Juice.cs:104-133: quadratic sweep from -90 to max(width, 200)+20.
    [Theory]
    [InlineData("friends-row:u_on", false, 520)]
    [InlineData("friends-row:u_on", true, 1040)]
    [InlineData("friends-request-in:u_req", false, 520)]
    [InlineData("friends-request-in:u_req", true, 1040)]
    public Task SheenMovesAcrossTheRowInTheWpfDuration(string tag, bool reduced, double duration) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var oldLevel = settings.MotionLevel;
        var oldGentle = settings.GentleVisuals;
        var oldProbe = UiMotionPreference.OsProbe;
        using var svc = FriendsDrawerTests.Service().Svc;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var window = new Window { Width = 420, Height = 650, Content = drawer };
        try
        {
            settings.MotionLevel = reduced ? ConditioningControlPanel.Models.MotionLevel.Reduced : ConditioningControlPanel.Models.MotionLevel.Full;
            settings.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => true;
            UiMotionPreference.CacheStamp = long.MinValue;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = Row(drawer, tag);
            var clock = new StepClock();
            row.SetValue(StepClock.Property, clock.Instance);
            Pointer(row, true);
            var band = row.GetLogicalDescendants().OfType<global::Avalonia.Controls.Shapes.Rectangle>()
                .Single(x => x.Tag as string == "friends-sheen");
            Assert.Same(clock.Instance, band.GetValue(StepClock.Property));
            var slide = Assert.IsType<TranslateTransform>(band.RenderTransform);
            double end = Math.Max(row.Bounds.Width, 200) + 20;
            double distance = end + 90;
            Assert.Equal(-90, slide.X, 4);
            clock.Step(0);
            clock.Step(duration / 4);
            Assert.Equal(-90 + distance * 0.125, slide.X, 4);
            clock.Step(duration / 2);
            Assert.Equal(-90 + distance * 0.5, slide.X, 4);
            // Avalonia removes a non-filled animation at completion, so check its final visible frame.
            clock.Step(duration - 1);
            Assert.InRange(slide.X, end - 0.01, end + 0.01);
            Assert.Contains(band, row.GetLogicalDescendants());
            clock.Step(duration);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(band, row.GetLogicalDescendants());
        }
        finally
        {
            window.Close(); settings.MotionLevel = oldLevel; settings.GentleVisuals = oldGentle;
            UiMotionPreference.OsProbe = oldProbe; UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

}
