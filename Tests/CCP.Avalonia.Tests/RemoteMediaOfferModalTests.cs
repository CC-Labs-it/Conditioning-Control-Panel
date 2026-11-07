using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMediaOfferModalTests
{
    // Drive the updater's existing ownership flag without a window, network request or font dependency.
    private static readonly FieldInfo Busy = typeof(AppUpdater).GetField("_offering", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_modal_parks_the_offer_without_spending_the_launch_claim(bool update) => WithOffer(opened =>
    {
        Busy.SetValue(null, update);
        MainShellWindow.IsStartupDialogShowing = !update;
        RemoteMediaOffer.Offer("video");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, opened());
        Assert.Empty(StartupLadder.Inbox.Items);
        RemoteMediaOffer.Offer("another-empty-surface");
        RemoteMediaOffer.FlushPending(); // still blocked: keep the request owed
        Assert.Equal(0, opened());
        Busy.SetValue(null, false);
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(1, opened());
        RemoteMediaOffer.FlushPending();
        StartupLadder.ResetForTests();
        RemoteMediaOffer.Offer("video");
        Assert.Equal(1, opened()); // one claim across every surface this launch
    });

    [Theory]
    [InlineData("online")]
    [InlineData("mixed")]
    public Task A_parked_offer_rechecks_the_source_before_flushing(string source) => WithOffer(opened =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.Offer("video");
        CoreSettings.Current.MediaSource = source;
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened());
        CoreSettings.Current.MediaSource = "local";
        RemoteMediaOffer.FlushPending();
        Assert.Equal(0, opened()); // dropped, not a stale request for the next launch
        RemoteMediaOffer.Offer("video");
        Assert.Equal(1, opened()); // the dropped request never spent the claim
    });

    [Fact]
    public Task An_Inbox_row_opened_during_an_update_is_parked_and_retried() => WithOffer(opened =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        RemoteMediaOffer.Offer("video");
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal(0, opened());
        Busy.SetValue(null, true);
        StartupLadder.Inbox.Open(row);
        Assert.Equal(0, opened());
        Busy.SetValue(null, false);
        RemoteMediaOffer.FlushPending();
        row = Assert.Single(StartupLadder.Inbox.Items);
        StartupLadder.Inbox.Open(row);
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task A_background_offer_is_marshaled_before_reading_modal_state() => WithOffer(opened =>
    {
        Task.Run(() => RemoteMediaOffer.Offer("video")).GetAwaiter().GetResult();
        Assert.Equal(0, opened());
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, opened());
    });

    [Fact]
    public Task An_Inbox_offer_does_not_open_after_the_source_changes() => WithOffer(opened =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        RemoteMediaOffer.Offer("video");
        var row = Assert.Single(StartupLadder.Inbox.Items);
        CoreSettings.Current.MediaSource = "online";
        StartupLadder.Inbox.Open(row);
        Assert.Equal(0, opened());
    });

    private static Task WithOffer(Action<Func<int>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldSource = CoreSettings.Current.MediaSource;
        var oldShow = RemoteMediaOffer.ShowCard;
        var oldBusy = AppUpdater.IsUpdateDialogActive;
        var oldStartup = MainShellWindow.IsStartupDialogShowing;
        StartupLadder.ResetForTests();
        RemoteMediaOffer.ResetForTests();
        var opened = 0;
        RemoteMediaOffer.ShowCard = _ => opened++;
        CoreSettings.Current.MediaSource = "local";
        Busy.SetValue(null, false);
        MainShellWindow.IsStartupDialogShowing = false;
        try { body(() => opened); }
        finally
        {
            Busy.SetValue(null, oldBusy);
            MainShellWindow.IsStartupDialogShowing = oldStartup;
            CoreSettings.Current.MediaSource = oldSource;
            RemoteMediaOffer.ShowCard = oldShow;
            RemoteMediaOffer.ResetForTests();
            StartupLadder.ResetForTests();
        }
        return Task.CompletedTask;
    });
}
