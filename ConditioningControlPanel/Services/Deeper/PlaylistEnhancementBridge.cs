using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// What the playlist bridge needs from an enhancement host. The app's
    /// implementation is <see cref="HostPlaylistEnhancementBinding"/>; tests use a fake.
    /// </summary>
    public interface IPlaylistEnhancementBinding
    {
        bool IsBound { get; }

        /// <summary>Loads the resolved enhancement and starts the engine on the source. False = nothing is bound.</summary>
        bool TryBind(ResolvedEnhancement resolved, PlaylistTimeSource source);

        /// <summary>Stops the engine and unloads. Safe when nothing is bound.</summary>
        void Unbind();
    }

    /// <summary>
    /// Runs a Deeper enhancement beside the playlist audio player: when the loaded
    /// track has an enhancement (embedded, sidecar or library, the same ladder the
    /// Deeper player walks) it is bound to a <see cref="PlaylistTimeSource"/> for
    /// that track, and it is torn down when the track changes or the player stops.
    /// Panic stops the playlist (contract rule), which reaches here as a stop.
    ///
    /// Single-owner rule: an <see cref="EnhancementHostService"/> holds one
    /// enhancement and one engine, and <c>App.DeeperHost</c> belongs to the Deeper
    /// player window. Like the video and browser bridges, this one owns its own
    /// host and never touches that one. On top of that it YIELDS: while the Deeper
    /// player's engine runs, the playlist binds nothing, and a playlist enhancement
    /// that is already running stops on the next tick. Two enhancements never
    /// drive the effects at once.
    /// </summary>
    public sealed class PlaylistEnhancementBridge : IDisposable
    {
        private readonly IAudioPlaylistService _playlist;
        private readonly IPlaylistEnhancementBinding _binding;
        private readonly Func<string, ResolvedEnhancement> _resolve;
        private readonly Func<bool> _deeperPlayerBusy;
        private readonly Func<bool>? _enabled;

        private PlaylistTimeSource? _source;
        private string? _boundPath;
        private string? _missPath;   // track already looked up with no enhancement
        private bool _disposed;

        public PlaylistEnhancementBridge(
            IAudioPlaylistService playlist,
            IPlaylistEnhancementBinding binding,
            Func<string, ResolvedEnhancement> resolve,
            Func<bool> deeperPlayerBusy,
            Func<bool>? enabled = null)
        {
            _enabled = enabled;
            _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            _deeperPlayerBusy = deeperPlayerBusy ?? throw new ArgumentNullException(nameof(deeperPlayerBusy));

            _playlist.TrackChanged += OnTrackChanged;
            _playlist.StateChanged += OnStateChanged;
            _playlist.PositionChanged += OnPosition;
            Sync();
        }

        /// <summary>Whole path of the track whose enhancement is running, or null.</summary>
        public string? BoundPath => _boundPath;

        private void OnTrackChanged(string? path)
        {
            // A new track always gets a fresh lookup, and the old one's engine never
            // outlives it, even when the same file is loaded again.
            _missPath = null;
            Unbind();
            Sync();
        }

        private void OnStateChanged() => Sync();

        /// <summary>
        /// The player's switch (AppSettings.AudioPlaylistEnhancements) moved. Off unbinds
        /// at once, mid-track too; on binds the playing track without waiting for a tick.
        /// </summary>
        public void Refresh() => Sync();

        private void OnPosition(double seconds)
        {
            // The Deeper player has no "bound" event, so the yield is checked on the
            // playlist's own tick (about 10 a second, one bool read).
            if (_boundPath != null)
            {
                // The switch is read here too, so a setting changed behind the card's
                // back (a restore, another window) still lets go within one tick.
                if (!IsEnabled() || IsDeeperPlayerBusy()) Unbind();
            }
            else
            {
                // Also the late bind: a service that raised TrackChanged before it was
                // playing, or the Deeper player letting go. A miss is remembered, so
                // this costs nothing on a track with no enhancement.
                Sync();
            }
        }

        /// <summary>Brings the binding in line with the playlist's state. Idempotent.</summary>
        private void Sync()
        {
            if (_disposed) return;
            try
            {
                // Switched off: nothing binds, nothing is looked up, and a running one stops.
                if (!IsEnabled()) { Unbind(); return; }

                bool playing = _playlist.IsPlaying;
                bool loaded = playing || _playlist.IsPaused;
                var path = loaded ? _playlist.CurrentPath : null;
                if (string.IsNullOrEmpty(path)) path = null;

                // Stopped (also panic and stop-all), nothing loaded, or another track.
                if (_boundPath != null && !SamePath(_boundPath, path)) Unbind();
                if (path == null) { _missPath = null; return; }

                if (IsDeeperPlayerBusy()) { Unbind(); return; }

                // A paused track keeps its engine (no ticks arrive); a new bind waits for play.
                if (_boundPath != null || !playing) return;
                if (SamePath(_missPath, path)) return;

                ResolvedEnhancement resolved;
                try { resolved = _resolve(path); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("PlaylistEnhancementBridge: resolve failed for {File}: {Error}",
                        Path.GetFileName(path), ex.Message);
                    _missPath = path;
                    return;
                }
                if (!resolved.Found) { _missPath = path; return; }

                var src = new PlaylistTimeSource(_playlist, path);
                if (!_binding.TryBind(resolved, src))
                {
                    src.Dispose();
                    _missPath = path;
                    return;
                }
                _source = src;
                _boundPath = path;
                App.Logger?.Information("PlaylistEnhancementBridge: bound enhancement ({Source}) for {File}",
                    resolved.Source, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PlaylistEnhancementBridge.Sync failed");
                Unbind();
            }
        }

        /// <summary>Stops the playlist's enhancement at once. Safe to call any time.</summary>
        public void Unbind()
        {
            if (_boundPath == null && _source == null && !_binding.IsBound) return;
            try { _binding.Unbind(); } catch { }
            try { _source?.Dispose(); } catch { }
            _source = null;
            _boundPath = null;
        }

        /// <summary>No switch handed in = always on. A switch that throws reads as off.</summary>
        private bool IsEnabled()
        {
            if (_enabled == null) return true;
            try { return _enabled(); } catch { return false; }
        }

        private bool IsDeeperPlayerBusy()
        {
            try { return _deeperPlayerBusy(); } catch { return false; }
        }

        private static bool SamePath(string? a, string? b) =>
            a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _playlist.TrackChanged -= OnTrackChanged; } catch { }
            try { _playlist.StateChanged -= OnStateChanged; } catch { }
            try { _playlist.PositionChanged -= OnPosition; } catch { }
            Unbind();
        }

        // -- App wiring --------------------------------------------------------

        private static PlaylistEnhancementBridge? _installed;
        private static DispatcherTimer? _installTimer;

        /// <summary>The app's bridge once the playlist service exists, else null.</summary>
        public static PlaylistEnhancementBridge? Installed => _installed;

        /// <summary>
        /// One call at startup. The playlist service may be created later in the same
        /// startup pass, so this waits for <c>App.AudioPlaylist</c> (a few short
        /// retries) and no-ops for good when the build has no playlist player.
        /// </summary>
        public static void Install()
        {
            if (_installed != null || _installTimer != null) return;
            if (TryInstallNow()) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            int triesLeft = 30;
            var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += (_, _) =>
            {
                if (TryInstallNow() || --triesLeft <= 0)
                {
                    timer.Stop();
                    _installTimer = null;
                }
            };
            _installTimer = timer;
            timer.Start();
        }

        private static bool TryInstallNow()
        {
            if (_installed != null) return true;
            var playlist = App.AudioPlaylist;
            if (playlist == null) return false;
            try
            {
                _installed = new PlaylistEnhancementBridge(
                    playlist,
                    new HostPlaylistEnhancementBinding(),
                    EnhancementResolver.ResolveForLocalMedia,
                    () => App.DeeperHost?.IsRunning == true,
                    () => App.Settings?.Current?.AudioPlaylistEnhancements != false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PlaylistEnhancementBridge.Install failed");
            }
            return true;
        }
    }

    /// <summary>
    /// The real binding: its own <see cref="EnhancementHostService"/> (never
    /// <c>App.DeeperHost</c>), loaded and bound the way the video bridge does it.
    /// </summary>
    public sealed class HostPlaylistEnhancementBinding : IPlaylistEnhancementBinding, IDisposable
    {
        private readonly EnhancementHostService _host = new();

        public bool IsBound => _host.IsRunning || _host.LoadedEnhancement != null;

        public bool TryBind(ResolvedEnhancement resolved, PlaylistTimeSource source)
        {
            bool loaded = resolved.FilePath != null
                ? _host.LoadFromFile(resolved.FilePath)
                : resolved.Enhancement != null
                    && _host.LoadFromMemory(resolved.Enhancement, "playlist:" + Path.GetFileName(source.TrackPath));
            if (!loaded) return false;

            if (_host.Bind(source, attach: source.Attach, detach: source.Detach)) return true;
            try { _host.Unload(); } catch { }
            return false;
        }

        public void Unbind()
        {
            try { _host.UnbindEngine(); } catch { }
            try { _host.Unload(); } catch { }
        }

        public void Dispose() => _host.Dispose();
    }
}
