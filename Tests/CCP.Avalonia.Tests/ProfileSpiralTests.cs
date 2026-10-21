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
        internal string Body = """{"user":{"descent":{"devotion_days":12,"stage":{"n":3,"next_at":21}}}}""";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
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
            for (int i = 0; i < 100 && !row.IsVisible; i++)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(plate.IsVisible);
            Assert.True(row.IsVisible);
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

            wire.Body = """{"user":{}}""";
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
}
