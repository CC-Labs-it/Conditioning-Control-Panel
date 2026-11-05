using System;
using System.Threading;

namespace ConditioningControlPanel.Services.Startup
{
    /// <summary>
    /// The budget behind WPF <c>App.OfferRemoteMediaSource</c> (ConditioningControlPanel/App.xaml.cs:879-987):
    /// an empty-assets dead end offers the remote-media card at most once per launch across every
    /// call site, never on top of a startup modal (parked and flushed later instead), and only while
    /// the user is still on local media. One extra gate on this head: the dead end's kind must have
    /// a remote pool that actually plays, so the card never points at a source this head cannot show.
    /// </summary>
    internal static class RemoteMediaOffer
    {
        public enum Step { Skip, Park, Offer }

        /// <summary>The coaching card's key (WPF App.xaml.cs:877).</summary>
        public const string IntroKey = "remotemedia";

        /// <summary>Set by the head once remote flashes play (lane row6-views-tab-assets--online-flash-media).</summary>
        public static bool FlashPoolLive { get; set; }

        /// <summary>Set by the head once remote mandatory videos play (lane row6-views-tab-assets--online-mandatory-video).</summary>
        public static bool VideoPoolLive { get; set; }

        private static int _claimed;
        private static string? _pending;

        /// <summary>What a dead end at <paramref name="surface"/> should do now. Offer spends the launch's one claim.</summary>
        public static Step Decide(string surface, string? mediaSource, bool modalUp)
        {
            // Already on the remote pool: the dead end is a different problem (WPF App.xaml.cs:906).
            if (!string.Equals(mediaSource, "local", StringComparison.OrdinalIgnoreCase)) return Step.Skip;
            if (!SourceLive(surface)) return Step.Skip;
            // Never stack on a startup modal: leave the budget unspent and park it (WPF App.xaml.cs:915).
            if (modalUp) { Park(surface); return Step.Park; }
            return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0 ? Step.Offer : Step.Skip;
        }

        private static bool SourceLive(string surface) => surface switch
        {
            "flashes" => FlashPoolLive,
            "videos" => VideoPoolLive,
            _ => FlashPoolLive || VideoPoolLive,
        };

        /// <summary>Records a dead end hit too early to show anything; the first one wins (WPF <c>??=</c>).</summary>
        public static void Park(string surface) => Interlocked.CompareExchange(ref _pending, surface, null);

        /// <summary>The parked surface, cleared (WPF FlushPendingRemoteMediaOffer, App.xaml.cs:969).</summary>
        public static string? TakePending() => Interlocked.Exchange(ref _pending, null);

        internal static void ResetForTests()
        {
            _claimed = 0;
            _pending = null;
            FlashPoolLive = false;
            VideoPoolLive = false;
        }
    }
}
