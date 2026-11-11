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
    public Task SwitchedOffOffersNeverOpenOrPark(bool modal) => WithOffer(opened =>
    {
        MainShellWindow.IsStartupDialogShowing = modal;
        RemoteMediaOffer.Offer("video");
        Task.Run(() => RemoteMediaOffer.Offer("background")).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        MainShellWindow.IsStartupDialogShowing = false;
        RemoteMediaOffer.FlushPending();
        Assert.Empty(StartupLadder.Inbox.Items);
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
