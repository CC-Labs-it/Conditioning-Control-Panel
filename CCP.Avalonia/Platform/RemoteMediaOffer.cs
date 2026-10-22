using System;
using System.Threading;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF <c>App.OfferRemoteMediaSource</c> (App.xaml.cs:828): at an empty-assets dead end, offer
    /// the online media source, at most once per launch. Only when the source is still "local";
    /// routed through <see cref="StartupLadder.PresentOrInbox"/> and the "remotemedia" intro card.
    /// The once-per-launch claim is spent when the card opens, so a parked Inbox row keeps it owed.
    /// Every failure is swallowed: the caller's own behaviour must happen either way.
    /// </summary>
    internal static class RemoteMediaOffer
    {
        private const string IntroKey = "remotemedia";
        private static int _claimed;

        /// <summary>Test seam: what opening the card does (default: the intro popup).</summary>
        internal static Action<Window?> ShowCard = owner => FeatureIntroPopup.ShowIfFirstTime(IntroKey, owner);

        /// <summary>Test seam: forget this launch's claim.</summary>
        internal static void ResetForTests() => Interlocked.Exchange(ref _claimed, 0);

        internal static void Offer(string surface, Window? owner = null)
        {
            try
            {
                if (!string.Equals(CoreSettings.Current.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) return;
                Log.Information("RemoteMedia: empty assets at {Surface} - offering the online source", surface);
                FeatureIntros.All.TryGetValue(IntroKey, out var card);
                StartupLadder.PresentOrInbox(new InboxItem
                {
                    Key = "intro:remote-media",
                    Glyph = card?.Glyph ?? "",
                    Title = "Media without the download",
                    Summary = "Your folders are empty - she can stream from the online pool instead.",
                    Open = () =>
                    {
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
