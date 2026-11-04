using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using AvApp = ConditioningControlPanel.Avalonia.App;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MysteryTileRefreshTests
{
    [Fact]
    public Task RevealTracksTheGiftAndPremiumState() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var dash = shell.SettingsPage!;
            shell.RefreshMysteryTile("takeover", false);
            Assert.Equal(Loc.Get("mosaic_free_today"), dash.CardMystery.Title);
            Assert.Null(dash.CardMystery.TierBadge);
            Assert.Equal(Loc.Get("tab_takeover"), dash.MysteryRevealName.Text);
            Assert.Equal(1, dash.MysteryRevealBadge.Tier);
            Assert.True(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("fyp", false);
            Assert.Equal(Loc.Get("tab_fyp"), dash.MysteryRevealName.Text);
            shell.RefreshMysteryTile("fyp", true);
            Assert.Equal(0, dash.MysteryRevealBadge.Tier);
            Assert.False(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("dtrh", false);
            Assert.Equal("Down the Rabbit Hole", dash.MysteryRevealName.Text);
            Assert.Equal(1, dash.MysteryRevealBadge.Tier);
            Assert.True(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("dtrh", true);
            Assert.Equal(0, dash.MysteryRevealBadge.Tier);
            Assert.False(dash.MysteryRevealBadge.FreeToday);
            shell.RefreshMysteryTile("unknown", false);
            Assert.Equal(Loc.Get("mosaic_daily_gift"), dash.MysteryRevealName.Text);
            Assert.Equal(0, dash.MysteryRevealBadge.Tier);
            Assert.False(dash.MysteryRevealBadge.FreeToday);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task HoverTurnsThePlateAndMotionOffStillReveals() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.DailyGiftLastRevealDate = "";
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            var dash = shell.SettingsPage!;
            var badgeScale = (ScaleTransform)((TransformGroup)dash.MysteryBadge.RenderTransform!).Children[0];
            shell.RefreshMysteryTile();
            time.Advance(TimeSpan.FromMilliseconds(900));
            BeatLoop.StepAll();
            Assert.Equal(0.95, dash.MysteryGlow.Opacity, 3);
            Assert.Equal(1.12, badgeScale.ScaleX, 3);
            Assert.Equal(badgeScale.ScaleX, badgeScale.ScaleY);
            settings.Current.MotionLevel = MotionLevel.Off;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(0.55, dash.MysteryGlow.Opacity, 3);
            Assert.Equal(1, badgeScale.ScaleX);
            Assert.True(dash.MysteryBadge.IsVisible);
            settings.Current.MotionLevel = MotionLevel.Full;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            time.Advance(TimeSpan.FromMilliseconds(900));
            BeatLoop.StepAll();
            Assert.Equal(0.95, dash.MysteryGlow.Opacity, 3);
            dash.IsVisible = false;
            Assert.Equal(0.55, dash.MysteryGlow.Opacity, 3);
            Assert.Equal(1, badgeScale.ScaleX);
            dash.IsVisible = true;
            time.Advance(TimeSpan.FromMilliseconds(900));
            BeatLoop.StepAll();
            Assert.Equal(0.95, dash.MysteryGlow.Opacity, 3);
            var host = dash.FindControl<Grid>("MysteryFlipHost")!;
            var plate = dash.FindControl<Grid>("MysteryFlipPlate")!;
            var scale = (ScaleTransform)((TransformGroup)plate.RenderTransform!).Children[0];
            var point = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), shell)!.Value;
            Assert.False(dash.MysteryRevealFace.IsVisible);
            shell.MouseMove(point, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), settings.Current.DailyGiftLastRevealDate);
            Assert.False(dash.MysteryBadge.IsVisible);
            Assert.Equal(0.55, dash.MysteryGlow.Opacity, 3);
            Assert.Equal(1, badgeScale.ScaleX);
            time.Advance(TimeSpan.FromMilliseconds(75));
            BeatLoop.StepAll();
            Assert.InRange(scale.ScaleX, 0.01, 0.99);
            time.Advance(TimeSpan.FromMilliseconds(100));
            BeatLoop.StepAll();
            Assert.True(dash.MysteryRevealFace.IsVisible);
            Assert.False(dash.CardMystery.IsVisible);
            time.Advance(TimeSpan.FromMilliseconds(150));
            BeatLoop.StepAll();
            Assert.Equal(1, scale.ScaleX);
            shell.MouseMove(new Point(1, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromMilliseconds(350));
            BeatLoop.StepAll();
            Assert.False(dash.MysteryRevealFace.IsVisible);
            Assert.True(dash.CardMystery.IsVisible);
            settings.Current.MotionLevel = MotionLevel.Off;
            shell.MouseMove(point, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dash.MysteryRevealFace.IsVisible);
            Assert.Equal(1, scale.ScaleX);
            shell.MouseMove(new Point(1, 1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.False(dash.MysteryRevealFace.IsVisible);
        }
        finally
        {
            shell.Close();
            BeatLoop.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RevealArtTracksGiftAndModChanges() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-gift-art-");
        var first = Path.Combine(dir.FullName, "first.png");
        var second = Path.Combine(dir.FullName, "second.png");
        using (var image = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) image.Save(first);
        using (var image = new WriteableBitmap(new PixelSize(6, 2), new Vector(96, 96))) image.Save(second);
        var previous = CoreModArt.OverridePathProvider;
        var property = typeof(AvApp).GetProperty("DailyFree", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldDaily = property.GetValue(null);
        property.SetValue(null, new DailyFreeService(fetchOverride: false));
        CoreModArt.OverridePathProvider = p => p == "features/fyp.png" ? second : p.StartsWith("features/") ? first : null;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var dash = shell.SettingsPage!;
            PixelSize Size() => Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(dash.MysteryRevealArt.Background).Source).PixelSize;
            shell.RefreshMysteryTile("takeover", false);
            var box = Size();
            var previousBrush = dash.MysteryRevealArt.Background;
            MainShellWindow.SetMysteryArt(dash.MysteryRevealArt, null);
            Assert.Same(previousBrush, dash.MysteryRevealArt.Background);
            shell.RefreshMysteryTile("fyp", false);
            Assert.NotEqual(box, Size());
            shell.RefreshMysteryTile("voice", false);
            Assert.Equal(box, Size());
            shell.RefreshMysteryTile("unknown", false);
            Assert.Equal(box, Size());
            string? requested = null;
            CoreModArt.OverridePathProvider = p => { requested = p; return first; };
            foreach (var (key, path) in new[] { ("takeover", "takeover"), ("awareness", "awareness"),
                ("haptics", "vibe"), ("fyp", "fyp"), ("remote", "remote_control"),
                ("dtrh", "dtrh"), ("voice", "mysterybox"), ("unknown", "mysterybox") })
            {
                shell.RefreshMysteryTile(key, false);
                Assert.Equal("features/" + path + ".png", requested);
            }
            CoreModArt.OverridePathProvider = p => p.StartsWith("features/") ? first : null;
            shell.RefreshMysteryTile();
            Assert.Equal(box, Size());
            CoreModArt.OverridePathProvider = p => p.StartsWith("features/") ? second : null;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(box, Size());
        }
        finally
        {
            shell.Close();
            property.SetValue(null, oldDaily);
            CoreModArt.OverridePathProvider = previous;
            dir.Delete(true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModArtSubscriptionExistsOnlyWhileTheShellIsOpen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        var field = typeof(CoreMods).GetField("ModChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
        int Subscriptions() => (field.GetValue(null) as Delegate)?.GetInvocationList().Count(d =>
            ReferenceEquals(d.Target, shell) && d.Method.Name == "OnModChangedFeatureImages") ?? 0;
        try
        {
            Assert.Equal(0, Subscriptions());
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Subscriptions());
            typeof(Window).GetMethod("OnOpened", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(shell, new object[] { EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Subscriptions());
        }
        finally { shell.Close(); }
        Assert.Equal(0, Subscriptions());
        return Task.CompletedTask;
    });

    private sealed class OverrideHandler : HttpMessageHandler
    {
        public int Calls;
        public string Key = "fyp";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"key\":\"" + Key + "\"}")
            });
        }
    }

    [Fact]
    public Task DashboardRefreshFetchesOverrideAndRepaints() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new DailyFreeService();
        var type = typeof(DailyFreeService);
        void Set(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, value);
        // An earlier server override is now beyond the six-hour freshness window.
        Set("_serverKey", "awareness");
        Set("_serverKeyForDate", DateTime.Now.ToString("yyyy-MM-dd"));
        Set("_lastFetchUtc", DateTime.UtcNow.AddHours(-7));
        var handler = new OverrideHandler { Key = "fyp" };
        Set("_http", new HttpClient(handler));
        var property = typeof(AvApp).GetProperty("DailyFree", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = property.GetValue(null);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.InitializeDailyFreeRefresh(service);
            property.SetValue(null, service);
            var dash = shell.SettingsPage!;
            shell.RefreshMysteryTile();
            var before = dash.MysteryRevealName.Text;
            Assert.Equal(Loc.Get("tab_awareness"), before);
            shell.RefreshFavoritesRail();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, handler.Calls);
            Assert.NotEqual(before, dash.MysteryRevealName.Text);
            Assert.Equal(MainShellWindow.MysteryName(handler.Key), dash.MysteryRevealName.Text);
            shell.RefreshFavoritesRail();
            Assert.Equal(1, handler.Calls); // six-hour success gate
        }
        finally
        {
            property.SetValue(null, previous);
            shell.Close();
        }
        return Task.CompletedTask;
    });

}
