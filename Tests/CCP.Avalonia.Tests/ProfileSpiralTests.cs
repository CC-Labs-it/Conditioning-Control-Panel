using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ProfileSpiralTests
{
    private sealed class Wire : HttpMessageHandler
    {
        internal const string Lit = """{"user":{"descent":{"devotion_days":12,"stage":{"n":3,"next_at":21}}}}""";
        internal const string Dark = """{"user":{}}""";
        internal string Body = Lit;
        internal int Hits;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Hits);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }

    [Fact]
    public Task ProfileDoorsFollowLiveBlockSelfAndWithhold_AndMenuNavigates() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, AccountSeed.NewV2, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = null;
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreAccount.IsLoggedInProvider = () => true;
        var s = service.Current;
        s.OfflineMode = true;
        s.MotionLevel = MotionLevel.Off;
        s.UnifiedId = "fake-user";
        s.UserDisplayName = "Spiral Test";
        s.AuthToken = "fake-token";
        s.DescentSpiralRailEnabled = false; // the web rail flag never gates the native doors
        var wire = new Wire();
        AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
        LocalizationManager.Instance.SetLanguage("en");
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            Dispatcher.UIThread.RunJobs();
            var page = shell.ProfilePage!;
            var plate = page.FindControl<Border>("ProfileSpiralPlate")!;
            var row = shell.Named<Button>("ProfileMenuSpiralRow")!;
            Assert.False(plate.IsVisible);
            Assert.False(row.IsVisible);
            s.OfflineMode = false;
            shell.UpdateQuickLoginUI();
            await shell.ProfileDescent!.RefreshAsync("signed in", force: true);
            Assert.True(await Until(() => plate.IsVisible)); // the sign-in ask may hold the fetch gate first
            Assert.True(row.IsVisible);
            // WPF ProfileVat.cs:133-170: the poll runs only while the card is on screen and the
            // window is presenting; minimising or leaving the card stops it.
            Assert.True(shell.ProfileSpiralPollRunning);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.ProfileSpiralPollRunning);
            shell.WindowState = WindowState.Normal;
            Assert.True(shell.ProfileSpiralPollRunning);
            shell.ShowTab("settings");
            Assert.False(shell.ProfileSpiralPollRunning);
            shell.ShowTab("discord");
            Assert.True(shell.ProfileSpiralPollRunning);
            Assert.Equal(Loc.GetF("programs_card_day", 12) + " · " + Loc.Get("descent_stage_3_name"),
                shell.Named<TextBlock>("ProfileMenuSpiralSummary")!.Text);

            shell.SetProfileViewingSelf(false);
            Assert.False(plate.IsVisible);
            Assert.True(row.IsVisible);
            shell.SetProfileViewingSelf(true);
            Assert.True(plate.IsVisible);
            s.DescentMigrationOffered = true;
            shell.RefreshProfileSpiralPlate(); shell.RefreshProfileMenuSpiral();
            Assert.False(plate.IsVisible);
            Assert.False(row.IsVisible);
            s.PendingDescentMigrationChoice = "restore";
            shell.RefreshProfileSpiralPlate(); shell.RefreshProfileMenuSpiral();
            Assert.True(plate.IsVisible);
            Assert.True(row.IsVisible);

            Assert.True(plate.IsEffectivelyVisible);
            using (var frame = shell.CaptureRenderedFrame())
                if (Environment.GetEnvironmentVariable("CCP_SPIRAL_PROOF") is { } png) frame!.Save(png);
            var at = plate.TranslatePoint(new Point(plate.Bounds.Width / 2, plate.Bounds.Height / 2), shell)!.Value;
            shell.MouseDown(at, global::Avalonia.Input.MouseButton.Left);
            shell.MouseUp(at, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal("spiral", shell.CurrentTab);
            shell.ShowTab("discord");
            shell.OpenProfileBubbleMenu();
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("spiral", shell.CurrentTab);
            Assert.False(shell.Named<global::Avalonia.Controls.Primitives.Popup>("ProfileBubblePopup")!.IsOpen);

            wire.Body = Wire.Dark;
            await shell.ProfileDescent.RefreshAsync("withdrawn", force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(plate.IsVisible);
            Assert.False(row.IsVisible);
            Assert.Empty(shell.Named<TextBlock>("ProfileMenuSpiralSummary")!.Text!);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, AccountSeed.NewV2, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = saved;
        }
    });

    // Waits on the pool's fire-and-forget asks, not on time: the floor runs on the fake clock (P08).
    private static async Task<bool> Until(Func<bool> done, int ms = 5000)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < ms)
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return true;
            await Task.Yield();
        }
        Dispatcher.UIThread.RunJobs();
        return done();
    }

    // WPF asks again, floored at 10 s, on profile load (MainWindow.xaml.cs:724) and on Trainer Card
    // open (MainWindow.ProfileVat.cs:111). The 60 s poll waits for HasSeenBlock, so without these a
    // same-account block that arrives mid-session never shows.
    [Fact]
    public Task SameAccountAsksAgainOnCardOpenAndProfileLoad_PastTheFloor() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, AccountSeed.NewV2, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = null;
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreAccount.IsLoggedInProvider = () => true;
        var s = service.Current;
        s.OfflineMode = true;
        s.MotionLevel = MotionLevel.Off;
        s.UnifiedId = "fake-user";
        s.UserDisplayName = "Spiral Test";
        s.AuthToken = "fake-token";
        var wire = new Wire { Body = Wire.Dark };
        AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
        LocalizationManager.Instance.SetLanguage("en");
        var shell = new MainShellWindow();
        AccountSeed.NewV2 = () => new V2AuthService(() => s, new Wire { Body = Wire.Dark }); // only the spiral reader counts
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        shell.ProfileDescent!.UtcNow = () => now;
        var profileLoaded = typeof(AccountSeed).GetField("ProfileLoaded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        try
        {
            shell.Show();
            shell.ShowTab("discord");
            var plate = shell.ProfilePage!.FindControl<Border>("ProfileSpiralPlate")!;
            s.OfflineMode = false;
            shell.UpdateQuickLoginUI();
            // Settle the sign-in race (fire-and-forget asks from before the flip, the sign-in ask):
            // one answered fetch for this account at the frozen clock, so every later ask is floored.
            for (var i = 0; i < 250 && !await shell.ProfileDescent.RefreshAsync("settle", force: true); i++)
                await Until(() => false, 20);
            var hits = wire.Hits;
            Assert.False(plate.IsVisible);                      // a dark account
            Assert.False(shell.ProfileSpiralPollRunning);       // no block seen, so no poll

            wire.Body = Wire.Lit;
            shell.ShowTab("settings"); shell.ShowTab("discord");
            Assert.False(await Until(() => wire.Hits > hits, 300)); // inside the 10 s floor: coalesced
            now += TimeSpan.FromSeconds(11);
            shell.ShowTab("settings"); shell.ShowTab("discord");
            Assert.True(await Until(() => plate.IsVisible));     // card open lights the dark key

            wire.Body = Wire.Dark;
            now += TimeSpan.FromSeconds(11);
            hits = wire.Hits;
            (profileLoaded.GetValue(null) as Action)!.Invoke();
            Assert.True(await Until(() => wire.Hits > hits && !plate.IsVisible)); // profile load re-reads
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, AccountSeed.NewV2, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = saved;
        }
    });
}
