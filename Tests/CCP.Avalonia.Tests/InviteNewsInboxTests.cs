using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Invites;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.InviteTicket.cs:123 (restack b160, from #4414): a /v2/invites/mine read files each
/// claim and each new subscription once, stamped before filing, with mapped icons, and opens the invites card.</summary>
public sealed class InviteNewsInboxTests
{
    [Fact]
    public Task InviteReadFilesOnceAndOpensInvites() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.Current.UnifiedId = "invite-test";
        CoreSettings.ServiceProvider = () => settings;
        StartupLadder.ResetForTests();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var slot = new InviteSlot("test-code", InviteSlotState.Trying, "Mia", null);
            var mine = new InviteMine(true, new InviteSnapshot(new[] { slot }, null, 0));
            shell.ApplyInviteTicket(mine);
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Null(InboxNoticeCard.Open);   // quiet window: filed, not presented
            Assert.True(IconMap.TryGet(row.Glyph, out var claimIcon));
            Assert.Equal(IconKind.TicketDiagonal, claimIcon.Kind);
            Assert.True(settings.Current.InboxStamps.ContainsKey("invite:code:test-code"));
            shell.ApplyInviteTicket(mine);
            Assert.Single(StartupLadder.Inbox.Items);

            shell.ApplyInviteTicket(new InviteMine(true, new InviteSnapshot(new[] { slot with { State = InviteSlotState.Converted } }, null, 1)));
            var subscribed = Assert.Single(StartupLadder.Inbox.Items.Where(item => item.Key != row.Key));
            Assert.True(IconMap.TryGet(subscribed.Glyph, out var subscriptionIcon));
            Assert.Equal(IconKind.Heart, subscriptionIcon.Kind);

            StartupLadder.Inbox.Open(row);
            var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
            var primary = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoticePrimary");
            primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("appsettings", shell.CurrentTab);
        }
        finally
        {
            InboxNoticeCard.Open?.Close();
            shell.Close();
            StartupLadder.ResetForTests();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
