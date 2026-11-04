using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>This head's WPF <c>App.OfferRemoteMediaSource</c> / <c>FlushPendingRemoteMediaOffer</c>
    /// (ConditioningControlPanel/App.xaml.cs:898, :965); the budget is Core <see cref="RemoteMediaOffer"/>.</summary>
    internal static class RemoteMediaOfferHead
    {
        /// <summary>Offers the remote source at an empty-assets dead end. Safe from any thread and never
        /// throws: the caller's own behaviour must happen whether or not the offer does.</summary>
        public static void Offer(string surface, Window? owner = null)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Offer(surface, owner)); return; }
            try
            {
                var modalUp = AppUpdater.IsUpdateDialogActive || MainShellWindow.IsStartupDialogShowing;
                if (RemoteMediaOffer.Decide(surface, CoreSettings.Current?.MediaSource, modalUp) != RemoteMediaOffer.Step.Offer) return;
                owner ??= (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                Log.Information("RemoteMedia: empty assets at {Surface}, offering the online source", surface);
                // Shown now, or an Inbox row in a quiet window; the seen flag is spent only when it opens (WPF App.xaml.cs:933).
                FeatureIntroPopup.ShowWhenStartupSettles(RemoteMediaOffer.IntroKey, owner, doorKey: null);
            }
            catch (Exception ex) { Log.Warning(ex, "RemoteMedia: offer failed at {Surface}", surface); }
        }

        /// <summary>Surfaces an offer parked by a site that ran too early or behind a modal. No-op when none is parked.</summary>
        public static void Flush(Window? owner = null)
        {
            if (RemoteMediaOffer.TakePending() is string surface) Offer(surface, owner);
        }
    }
}
