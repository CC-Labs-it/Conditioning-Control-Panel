using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.xaml.cs:891: one offer per launch, deferred behind startup modals.</summary>
internal static class RemoteMediaOffer
{
    private static int _claimed;
    private static bool _pending;
    private static Window? _owner;

    internal static void Offer(Window? owner)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Offer(owner)); return; }
        if (CoreSettings.Current.MediaSource != "local" || Volatile.Read(ref _claimed) != 0) return;
        if (MainShellWindow.IsStartupDialogShowing || AppUpdater.IsUpdateDialogActive)
        {
            _owner = owner;
            if (_pending) return;
            _pending = true;
            DispatcherTimer.Run(() =>
            {
                if (!_pending) return false;
                FlushPending();
                return _pending;
            }, TimeSpan.FromMilliseconds(250));
            return;
        }
        StartupLadder.PresentOrInbox(new InboxItem
        {
            Key = "intro:remote-media",
            Glyph = "🌐",
            Title = "Media without the download",
            Summary = "Your folders are empty - she can stream from the online pool instead.",
            Open = () =>
            {
                if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
                FeatureIntroPopup.ShowIfFirstTime("remotemedia", owner?.IsVisible == true ? owner : null);
            },
        });
    }

    internal static void FlushPending()
    {
        if (!_pending || MainShellWindow.IsStartupDialogShowing || AppUpdater.IsUpdateDialogActive) return;
        _pending = false;
        Offer(_owner);
    }

    internal static void ResetForTests() { _claimed = 0; _pending = false; _owner = null; }
}
