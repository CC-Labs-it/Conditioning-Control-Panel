using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellAccountShellTests
{
    [Fact]
    public Task UpdateStatusFromAWorkerPaintsThePillOnTheUiThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var shell = new MainShellWindow();
        try
        {
            var pill = shell.Named<Button>("BtnUpdateAvailable")!;
            var uiThread = Environment.CurrentManagedThreadId;
            var paintedThread = 0;
            pill.PropertyChanged += (_, e) =>
            {
                if (e.Property == ContentControl.ContentProperty)
                    paintedThread = Environment.CurrentManagedThreadId;
            };
            foreach (var available in new[] { true, false })
            {
                await Task.Run(() => shell.ShowUpdateAvailableButton(available)).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(uiThread, paintedThread);
                Assert.Equal(available ? "UpdateAvailable" : "NoUpdate", pill.Tag);
                Assert.Equal(Loc.Get(available ? "btn_update_now" : "btn_update_up_to_date"), pill.Content);
                Assert.Equal(Loc.Get(available ? "tooltip_update_now" : "tooltip_update_up_to_date"), ToolTip.GetTip(pill));
            }
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData("  Display Name  ", "Provider Name", "Discord Name", false, null, "Display Name")]
    [InlineData("  ", "  Provider Name  ", "Discord Name", false, null, "Provider Name")]
    [InlineData(null, "  ", "  Discord Name  ", false, null, "Discord Name")]
    [InlineData(null, null, null, true, null, null)]
    [InlineData(null, null, null, false, "sample-account", null)]
    [InlineData(null, null, null, false, null, null)]
    public void AccountRefreshAndLogoutRepaintTheWelcomeBanner(string? name, string? providerName,
        string? discordName, bool signedIn, string? unifiedId, string? expectedName) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null) RenderProof.EnsureSetUp();
        var s = CoreSettings.Current;
        var before = (s.UserDisplayName, s.UnifiedId, s.OfflineMode, s.OfflineUsername);
        var oldSignedIn = CoreAccount.IsLoggedInProvider;
        var patreonSlot = typeof(AccountSeed).GetProperty("Patreon", BindingFlags.Static | BindingFlags.NonPublic)!;
        var discordSlot = typeof(AccountSeed).GetProperty("Discord", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldPatreon = AccountSeed.Patreon;
        var oldDiscord = AccountSeed.Discord;
        var provider = new ProviderSubscription("patreon", () => s) { DisplayName = providerName };
        var discord = new DiscordAccount(() => provider);
        // Fixture injection only: the seed owns these setters; cached state stays in the test sandbox.
        typeof(DiscordAccount).GetProperty(nameof(DiscordAccount.DisplayName))!.SetValue(discord, discordName);
        patreonSlot.SetValue(null, provider);
        discordSlot.SetValue(null, discord);
        CoreAccount.IsLoggedInProvider = () => signedIn;
        (s.UserDisplayName, s.UnifiedId, s.OfflineMode) = (name, unifiedId, false);
        var shell = new MainShellWindow();
        try
        {
            var banner = shell.Named<TextBlock>("TxtBannerSecondary")!;
            shell.SetBannerAnnouncement("Previous account");
            shell.UpdateQuickLoginUI(); // sign-in, restore, link/unlink and logout all reach this real refresh path
            Assert.Equal(expectedName is not null ? Loc.GetF("label_welcome_back_0", expectedName)
                : Loc.Get(signedIn || unifiedId is not null ? "banner_signed_in_no_name"
                    : "label_welcome_consider_logging_in_with_patreon_for"), banner.Text);

            (s.OfflineMode, s.OfflineUsername) = (true, "Offline User");
            shell.UpdateQuickLoginUI();
            Assert.Equal(Loc.GetF("label_welcome_back_0_offline_mode", "Offline User"), banner.Text);
        }
        finally
        {
            shell.Close();
            (s.UserDisplayName, s.UnifiedId, s.OfflineMode, s.OfflineUsername) = before;
            CoreAccount.IsLoggedInProvider = oldSignedIn;
            patreonSlot.SetValue(null, oldPatreon);
            discordSlot.SetValue(null, oldDiscord);
        }
    });

}
