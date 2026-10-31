using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Invites;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SyncSevenTests
{
    private static void Setup()
    {
        if (Application.Current == null)
        {
            var fonts = AppFonts.Options(systemEmoji: false);
            ((System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>)fonts.FontFamilyMappings!)["Segoe UI Black"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
            ((System.Collections.Generic.Dictionary<string, global::Avalonia.Media.FontFamily>)fonts.FontFamilyMappings!)["Segoe UI"] = new global::Avalonia.Media.FontFamily(AppFonts.Inter);
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(fonts).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public Task EmptyRepeatCardRequiresAndAcceptsOneAnswer(int repeats) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", repeats, strictMode: true, isTest: true);
            var card = LockCardWindow.Primary!;
            Assert.Equal(1, card.RequiredRepeats);
            card.FindControl<TextBox>("TxtInput")!.Focus();
            foreach (var c in "obey") { card.KeyTextInput(c.ToString()); Dispatcher.UIThread.RunJobs(); }
            Assert.True(card.IsCompleted);
        }
        finally { LockCardWindow.ForceCloseAll(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimedFlashPathsUseTheSliderCeiling(bool ambient)
    {
        var s = new AppSettings { HydraLimit = 8, FlashStayUntilPopped = false };
        Assert.Equal(8, FlashOverlay.Cap(s, ambient));
        s.HydraLimit = 20;
        Assert.Equal(20, FlashOverlay.Cap(s, ambient));
    }

    [Fact]
    public Task InviteReadQueuesNewsOnceDuringQuietTime() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        StartupLadder.ResetForTests();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        CoreSettings.Current.UnifiedId = "sync-seven";
        CoreSettings.Current.InboxStamps.Clear();
        var shell = new MainShellWindow();
        try
        {
            var mine = new InviteMine(true, new InviteSnapshot(new[] { new InviteSlot("SYNC-CODE", InviteSlotState.Trying, "Ann", 1) }, null, 0));
            shell.ApplyInviteTicket(mine);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "invite:code:SYNC-CODE");
            shell.ApplyInviteTicket(mine);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "invite:code:SYNC-CODE");
        }
        finally { shell.Close(); StartupLadder.ResetForTests(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("escape")]
    [InlineData("close")]
    [InlineData("invites")]
    public Task InviteNewsPresentsAModelessCardWithDismissalAndAction(string action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        StartupLadder.ResetForTests();
        CoreTutorial.IsActiveProvider = () => false;
        CoreSession.IsSessionRunningProvider = () => false;
        CoreSettings.Current.UnifiedId = "sync-seven";
        CoreSettings.Current.InboxStamps.Clear();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("settings");
            shell.ApplyInviteTicket(new InviteMine(true, new InviteSnapshot(new[] {
                new InviteSlot("SYNC-CARD", InviteSlotState.Trying, "Ann", 1) }, null, 0)));
            var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
            Assert.True(card.IsVisible);
            Assert.False(card.IsDialog);
            Assert.True(shell.IsEnabled);
            Assert.Same(shell, card.Owner);
            Assert.Equal(WindowDecorations.None, card.WindowDecorations);
            Assert.False(card.ShowInTaskbar);
            Assert.False(card.CanResize);
            Assert.Equal(400, card.Width);
            Assert.Equal(SizeToContent.Height, card.SizeToContent);
            Assert.Empty(StartupLadder.Inbox.Items);
            var buttons = card.GetVisualDescendants().OfType<Button>().ToArray();
            var close = buttons.Single(b => b.Name == "NoticeClose");
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("vaultgate_close"), Assert.IsType<TextBlock>(close.Content).Text);
            if (action == "escape") card.RaiseEvent(new global::Avalonia.Input.KeyEventArgs {
                RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Escape });
            else (action == "close" ? close : buttons.Single(b => b.Name == "NoticePrimary"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(card.IsVisible);
            Assert.Null(InboxNoticeCard.Open);
            Assert.True(shell.IsEnabled);
            Assert.Equal(action == "invites", shell.FindControl<ConditioningControlPanel.Avalonia.Views.Tabs.ExclusivesTabView>("ExclusivesTab")!.IsVisible);
        }
        finally { InboxNoticeCard.Open?.Close(); shell.Close(); StartupLadder.ResetForTests(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task SpentIntakeToastOpensBasicPricedCard(bool reconnect, bool locked) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasPremiumAccessProvider = () => false;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
        CoreSettings.Current.IntakePassSpentUtc = DateTime.UtcNow;
        var shell = new MainShellWindow();
        var reconnects = 0;
        TierGate.ReconnectIsTheAnswerProvider = () => reconnect;
        shell.ReconnectFromGateOverride = () => { reconnects++; return Task.CompletedTask; };
        var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            shell.Show();
            if (locked) lockdown.Activate(TimeSpan.FromMinutes(30));
            ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(shell.FindControl<StackPanel>("NotificationHost")!);
            var view = shell.FindControl<GradedIntakeTabView>("GradedIntakeTab")!;
            view.BtnStartIntake_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            var action = shell.GetVisualDescendants().OfType<Button>().Last(b => b.Name == "ToastAction");
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (locked || reconnect) Assert.Null(VaultGateDialog.Open);
            else
            {
                Assert.NotNull(VaultGateDialog.Open);
                Assert.Equal(1, (int)typeof(VaultGateDialog).GetField("_tier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(VaultGateDialog.Open)!);
            }
            Assert.Equal(reconnect && !locked ? 1 : 0, reconnects);
        }
        finally { lockdown.Deactivate(); VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });
}
