using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Serilog;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The now-playing watcher, on Windows' System Media Transport Controls — the single richest cheap
    /// signal on this platform (doc 02 §2.2). It is what turns "you're on Spotify" into "that is the
    /// fourth time round on the same song".
    ///
    /// <para><b>Defensive by design.</b> SMTC is denied outright on some machines, absent on others,
    /// and its session objects can be torn down between two calls. Every path here is wrapped, the
    /// manager is requested asynchronously exactly once, and any failure simply leaves
    /// <see cref="IsAvailable"/> false — awareness losing the media signal is one missing joke, never
    /// an error the user sees.</para>
    ///
    /// <para><b>Privacy.</b> Titles and artists read here are machine-local. They reach
    /// <see cref="ContextFrame.NowPlaying"/>, from which the LOCAL projection may include them and the
    /// CLOUD projection includes only the repeat count (<see cref="AwarenessProjection"/>). Nothing
    /// here is ever written to disk: <see cref="ActivityLedger"/> has no parameter that could carry a
    /// track title, and this class never touches a file.</para>
    /// </summary>
    public sealed class SmtcMediaWatcher : IMediaWatcher
    {
        /// <summary>How often the current session is re-read. Media does not need 1.5s resolution.</summary>
        public static readonly TimeSpan PollInterval = MediaSessionWatcher.PollInterval;

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private readonly MediaSessionWatcher _watcher;
        private volatile bool _disposed;

        public SmtcMediaWatcher() => _watcher = new MediaSessionWatcher(InitializeAsync, ReadAsync);
        public MediaSample? Current => _watcher.Current;
        public bool IsAvailable => _watcher.IsAvailable;
        public void Start() => _watcher.Start();
        public void Stop() => _watcher.Stop();

        public void Dispose()
        {
            _disposed = true;
            _watcher.Dispose();
            _manager = null;
        }

        private async Task<bool> InitializeAsync()
        {
            if (_manager != null) return true;
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_disposed) return false;
                _manager = manager;
                return manager != null;
            }
            catch (Exception ex)
            {
                Log.Debug("AwarenessObserver: SMTC unavailable - {Error}", ex.Message);
                return false;
            }
        }

        private async Task<MediaSample?> ReadAsync()
        {
            var manager = _manager;
            if (manager == null) return null;

            GlobalSystemMediaTransportControlsSession? session;
            try { session = manager.GetCurrentSession(); }
            catch { return null; }

            if (session == null) return null;

            string title;
            string? artist;
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (props == null) return null;
                title = props.Title ?? "";
                artist = props.Artist;
            }
            catch { return null; }

            var sample = MediaSample.FromSession(title, artist, null, TimeSpan.Zero);
            if (sample == null) return null;

            string state = "Unknown";
            try { state = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "Unknown"; }
            catch { }

            var position = TimeSpan.Zero;
            try { position = session.GetTimelineProperties()?.Position ?? TimeSpan.Zero; }
            catch { }

            return sample with { PlaybackState = state, Position = position };
        }
    }
}
