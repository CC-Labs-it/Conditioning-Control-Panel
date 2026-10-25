using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartupModalTests
{
    private static void Run(Action<Window, StartupLadder.TestClock> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        var owner = new Window();
        owner.Show();
        StartupLadder.Owner = () => owner;
        try { body(owner, (StartupLadder.TestClock)StartupLadder.Time); }
        finally { StartupLadder.StopForPanic(); owner.Close(); StartupLadder.ResetForTests(); }
    });

    [Fact]
    public void OrdersDedupesAndWaitsForTheDialogBeforeDrainingPassive() => Run((owner, clock) =>
    {
        var seen = new List<string>();
        var done = new TaskCompletionSource();
        StartupLadder.EnqueueModal("later", 40, _ => { seen.Add("later"); return Task.CompletedTask; });
        StartupLadder.EnqueueModal("first", 5, _ => { seen.Add("first"); return done.Task; });
        StartupLadder.EnqueueModal("FIRST", 5, _ => throw new Exception("duplicate"));
        StartupLadder.PresentOrInbox(new() { Key = "passive", Title = "Passive", Open = () => seen.Add("passive") });
        Assert.Empty(seen);
        Assert.Empty(StartupLadder.Inbox.Items);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "first" }, seen);
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(seen);
        done.SetResult();
        Dispatcher.UIThread.RunJobs();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(new[] { "first", "later", "passive" }, seen);
        Assert.True(StartupLadder.IsLadderIdle);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
    });

    [Fact]
    public void HiddenOwnerWaitsAndPanicDropsPendingOffers() => Run((owner, clock) =>
    {
        int opened = 0, abandoned = 0;
        owner.Hide();
        StartupLadder.EnqueueModal("ask", 40, _ => { opened++; return Task.CompletedTask; }, () => abandoned++);
        int passive = 0;
        StartupLadder.PresentOrInbox(new() { Key = "held", Title = "Held", Open = () => passive++ });
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, opened);
        MainShellWindow.IsStartupDialogShowing = true;   // the wizard's own flag, not the ladder's
        try
        {
            PanicSurfaces.StopAll("test");
            Assert.True(MainShellWindow.IsStartupDialogShowing);
        }
        finally { MainShellWindow.IsStartupDialogShowing = false; }
        owner.Show();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, opened);
        Assert.Equal(1, abandoned);
        Assert.Equal(1, passive);   // the held surface is not stranded by the panic
        Assert.True(StartupLadder.IsLadderIdle);
    });

    [Fact]
    public void DrainUnderASettlingCardStillOpensTheHeldSurface() => Run((owner, clock) =>
    {
        var seen = new List<string>();
        StartupLadder.PresentOrInbox(new() { Key = "card", Title = "Card", Open = () => seen.Add("card") });
        StartupLadder.EnqueueModal("modal", 40, _ => { seen.Add("modal"); return Task.CompletedTask; });
        StartupLadder.PresentOrInbox(new() { Key = "held", Title = "Held", Open = () => seen.Add("held") });
        clock.Advance(TimeSpan.FromSeconds(1));   // the modal drains inside the card's 3 s settle
        Assert.Equal(new[] { "card", "modal" }, seen);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "card", "modal", "held" }, seen);
    });

    [Fact]
    public void RestoredNamelessAccountAsksOnceAndPanicClosesThePrompt() => Run((owner, clock) =>
    {
        var settings = CoreSettings.Current;
        var (uid, name) = (settings.UnifiedId, settings.UserDisplayName);
        var world = FriendsLanding.World;
        try
        {
            FriendsLanding.World = () => new(false, false, false, true, false, false, false);
            settings.UnifiedId = "test-account";
            settings.UserDisplayName = "";
            var ask = new RequiredNameAsk();
            ask.ProfileLoaded();
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Single(owner.OwnedWindows.OfType<DisplayNameDialog>());
            PanicSurfaces.StopAll("test");
            Assert.Empty(owner.OwnedWindows);
            ask.ProfileLoaded();
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.Empty(owner.OwnedWindows);
        }
        finally { (settings.UnifiedId, settings.UserDisplayName) = (uid, name); FriendsLanding.World = world; }
    });

    [Fact]
    public void RequiredModeValidatesAndKeepsARefusedSaveOpen() => Run((owner, clock) =>
    {
        int saves = 0;
        var dialog = new DisplayNameDialog("ab", _ => { saves++; return Task.FromResult<string?>("Name taken"); });
        _ = dialog.ShowDialog<bool>(owner);
        try
        {
            var confirm = dialog.FindControl<Button>("BtnConfirm")!;
            Assert.False(confirm.IsEnabled);
            Assert.False(dialog.FindControl<Button>("BtnCancel")!.IsVisible);
            var status = dialog.FindControl<TextBlock>("TxtStatus")!;
            dialog.FindControl<TextBox>("TxtDisplayName")!.Text = "bad!name";
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsEnabled);
            Assert.True(status.IsVisible);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("name_ask_bad_chars"), status.Text);
            dialog.FindControl<TextBox>("TxtDisplayName")!.Text = "valid-name";
            Dispatcher.UIThread.RunJobs();
            Assert.True(confirm.IsEnabled);
            Assert.False(status.IsVisible);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, saves);
            Assert.True(dialog.IsVisible);
            Assert.Equal("Name taken", dialog.FindControl<TextBlock>("TxtStatus")!.Text);
            Assert.True(confirm.IsEnabled);
        }
        finally { dialog.Close(false); }
    });
}
