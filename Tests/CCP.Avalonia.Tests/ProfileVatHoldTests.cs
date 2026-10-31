using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileVatHoldTests
{
    [Fact]
    public Task CardEntryAndServerChangesApplyThePersistedHoldWithoutRevealingOrPouring() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new global::ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.UnifiedId = "vat-hold";
        s.OfflineMode = false;
        s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
        s.VatPouredAccount = s.UnifiedId;
        s.VatPouredDayUtc = DateTime.UtcNow.ToString("yyyy-MM-dd");
        s.VatPouredTodayXp = 600;
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
        CoreSecrets.StoreProvider = (_, _) => { };
        JObject? reply = Wire(1000, 4000);
        var service = new DescentService(_ => Task.FromResult(reply));
        global::ConditioningControlPanel.Avalonia.App.Descent = service;
        MainShellWindow? shell = null;
        try
        {
            await service.RefreshAsync("seed before shell", force: true);
            shell = new MainShellWindow(); shell.Show(); shell.ShowTab("discord");
            Dispatcher.UIThread.RunJobs();
            var glass = shell.ProfilePage.FindControl<VatGlassCanvas>("ProfileVatGlass")!;
            Assert.Equal(400, shell.VatHeldXp);
            Assert.Equal(.15, glass.Fill, 6); // Only the persisted 600 XP are in the glass.
            Assert.False(glass.IsVisible);
            Assert.False(glass.IsPouring);
            Assert.False(shell.VatPollRunning);
            Assert.DoesNotContain("descent-vat", s.SeenFeatureIntros);

            glass.SnapTo(.2);
            Assert.Contains(800.ToString("N0"), shell.ProfilePage.FindControl<TextBlock>("ProfileVatReadout")!.Text);
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.15, glass.Fill, 6); // Re-entry restores the hold even on an unchanged reading.
            shell.ShowTab("settings"); Dispatcher.UIThread.RunJobs();
            reply = Wire(1400, 4000);
            await service.RefreshAsync("off-screen earn", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(800, shell.VatHeldXp);
            Assert.Equal(.15, glass.Fill, 6);
            Assert.Equal(600, s.VatPouredTodayXp);
            shell.ShowTab("discord"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.15, glass.Fill, 6);
            Assert.Equal(800, shell.VatHeldXp);

            reply = Wire(1400, 8000);
            await service.RefreshAsync("cap retune", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(.075, glass.Fill, 6);
            Assert.Equal(800, shell.VatHeldXp);
            Assert.Contains(8000.ToString("N0"), shell.ProfilePage.FindControl<TextBlock>("ProfileVatReadout")!.Text);

            reply = Wire(0, 8000);
            await service.RefreshAsync("daily drain", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, shell.VatHeldXp);
            Assert.Equal(0, glass.Fill);
            Assert.Equal(0, s.VatPouredTodayXp);
            Assert.False(glass.IsPouring);

            reply = Wire(1000, 4000);
            await service.RefreshAsync("earn again", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1000, shell.VatHeldXp);
            reply = new JObject();
            await service.RefreshAsync("withdraw", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, shell.VatHeldXp);
            Assert.False(glass.IsVisible);
            Assert.False(shell.VatPollRunning);
            Assert.Equal(104, shell.ProfilePage.FindControl<global::ConditioningControlPanel.Avalonia.Views.Controls.AdornedAvatar>("ProfileHeroAvatar")!.AvatarSize);
            reply = Wire(1000, 4000);
            await service.RefreshAsync("reseed", force: true); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1000, shell.VatHeldXp);
            Assert.Equal(0, glass.Fill);
            Assert.False(glass.IsPouring);
        }
        finally { s.OfflineMode = true; service.Reset(); shell?.Close(); }
    });

    private static JObject Wire(int xp, int cap) => JObject.Parse(
        "{\"descent\":{\"devotion_days\":3,\"vat\":{\"cap\":" + cap + ",\"today_xp\":" + xp + ",\"fill_lip_pct\":120}}}");
}
