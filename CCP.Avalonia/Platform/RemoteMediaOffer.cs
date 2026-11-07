using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF <c>App.OfferRemoteMediaSource</c> (App.xaml.cs:891): at an empty-assets dead end, offer
    /// the online media source, at most once per launch. Only when the source is still "local";
    /// routed through <see cref="StartupLadder.PresentOrInbox"/> and the "remotemedia" intro card.
    /// The once-per-launch claim is spent when the card opens, so a parked Inbox row keeps it owed.
    /// Every failure is swallowed: the caller's own behaviour must happen either way.
    /// </summary>
    internal static class RemoteMediaOffer
    {
        private const string IntroKey = "remotemedia";
        private static int _claimed;
        private static (string Surface, Window? Owner)? _pending;
        private static bool ModalUp => AppUpdater.IsUpdateDialogActive || MainShellWindow.IsStartupDialogShowing;

        /// <summary>Test seam: what opening the card does (default: the intro popup).</summary>
        internal static Action<Window?> ShowCard = owner => FeatureIntroPopup.ShowIfFirstTime(IntroKey, owner);

        /// <summary>Test seam: forget this launch's claim.</summary>
        internal static void ResetForTests()
        {
            Interlocked.Exchange(ref _claimed, 0);
            _pending = null;
        }

        /// <summary>WPF FlushPendingRemoteMediaOffer: recheck the source and modal gates after
        /// the updater or first-run wizard releases the screen. A second modal re-parks it.</summary>
        internal static void FlushPending()
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(FlushPending); return; }
                if (_pending is not { } pending) return;
                _pending = null;
                Offer(pending.Surface, pending.Owner);
            }
            catch (Exception ex) { Log.Warning(ex, "RemoteMedia: pending offer flush failed"); }
        }

        internal static void Offer(string surface, Window? owner = null)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Offer(surface, owner)); return; }
                if (!string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return;
                // WPF App.OfferRemoteMediaSource: do not let the intro's modal guard silently
                // consume this launch's offer. Keep the first request until the modal unwinds.
                if (ModalUp) { _pending ??= (surface, owner); return; }
                Log.Information("RemoteMedia: empty assets at {Surface} - offering the online source", surface);
                FeatureIntros.All.TryGetValue(IntroKey, out var card);
                StartupLadder.PresentOrInbox(new InboxItem
                {
                    Key = "intro:remote-media",
                    Glyph = card?.Glyph ?? "",
                    Title = Loc.Get("remote_media_offer_title"),
                    Summary = Loc.Get("remote_media_offer_summary"),
                    Open = () =>
                    {
                        // A held passive card / Inbox row can be opened after another modal starts.
                        if (ModalUp) { _pending ??= (surface, owner); return; }
                        if (!string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return;
                        if (Interlocked.CompareExchange(ref _claimed, 1, 0) != 0) return;
                        ShowCard(owner);
                    },
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RemoteMedia: offer failed at {Surface}", surface);
            }
        }
    }
}
