using System;
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
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            var dash = shell.SettingsPage!;
            var host = dash.FindControl<Grid>("MysteryFlipHost")!;
            var plate = dash.FindControl<Grid>("MysteryFlipPlate")!;
            var scale = (ScaleTransform)((TransformGroup)plate.RenderTransform!).Children[0];
            var point = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), shell)!.Value;
            Assert.False(dash.MysteryRevealFace.IsVisible);
            shell.MouseMove(point, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), settings.Current.DailyGiftLastRevealDate);
            Assert.False(dash.MysteryBadge.IsVisible);
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
