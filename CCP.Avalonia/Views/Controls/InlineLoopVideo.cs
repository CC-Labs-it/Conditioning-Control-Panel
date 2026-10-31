using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// WPF Controls/InlineLoopVideo.cs: a small muted, looping video surface drawn into an
    /// <see cref="Image"/> (no VideoView), on the shared LibVLC through <see cref="VlcFrameSink"/>.
    ///
    /// <para>Lifecycle follows the visual tree, which is where WPF's callers drove it by hand
    /// (HelpPopover: Resume on open, Pause on close, Dispose on detach): attach starts decoding,
    /// IsVisible=false pauses, detach stops and frees the player, buffer and bitmap. An Avalonia
    /// popup detaches its content on close, so a re-open starts the clip again from the top where
    /// WPF resumed a paused player. Fail-soft like WPF: no clip or no LibVLC leaves it blank.</para>
    ///
    /// <para>Only this control's own IsVisible pauses it: a hidden ANCESTOR leaves it decoding (P01).
    /// HelpPopover detaches on close; GazeMinigame removes both panes before feedback and on close.
    /// Other hosts that hide a parent must call <see cref="Pause"/>/<see cref="Resume"/> themselves.</para>
    /// </summary>
    public sealed class InlineLoopVideo : Image
    {
        private readonly string? _path;
        internal bool Loops { get; }
        private MediaPlayer? _player;
        private Media? _media;
        private VlcFrameSink? _sink;

        /// <summary>Render-proof constructor: no clip, a blank surface.</summary>
        public InlineLoopVideo() : this(null) { }

        /// <param name="randomStart">WPF GazeMinigameWindow.xaml.cs:976-989: once playing, jump to a
        /// random point in the first half of the clip.</param>
        /// <param name="loop">False for WPF gaze rounds, which play once and retain the last frame.</param>
        public InlineLoopVideo(string? clipPath, bool randomStart = false, bool loop = true)
        {
            _path = clipPath;
            Loops = loop;
            if (randomStart) StartOffset = (float)(Random.Shared.NextDouble() * 0.5);
            Stretch = Stretch.UniformToFill;
        }

        /// <summary>A player exists (attached, clip found, LibVLC loaded).</summary>
        internal string? ClipPath => _path;
        internal bool HasPlayer => _player != null;
        internal bool IsPlaying => _player?.IsPlaying == true;
        internal long Time => _player?.Time ?? -1;
        internal int FrameCount => _sink?.FrameCount ?? 0;
        /// <summary>Position (0-0.5) applied on the first Playing event; null = start at 0.</summary>
        internal float? StartOffset { get; }
        /// <summary>UI thread, per blitted frame: lets tests wait on frames, not on a clock.</summary>
        internal event Action? FrameShown;
        /// <summary>Players created and not yet disposed, process-wide: the leak check.</summary>
        internal static int LivePlayers;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (IsVisible) Resume();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            Teardown();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty || !this.IsAttachedToVisualTree()) return;
            if (IsVisible) Resume(); else Pause();
        }

        /// <summary>Start (first call) or resume decoding.</summary>
        public void Resume()
        {
            if (_player == null) { TryStart(); return; }
            try { _player.SetPause(false); } catch { /* native race */ }
        }

        /// <summary>Stop decoding but keep the player for a later <see cref="Resume"/>.</summary>
        public void Pause()
        {
            try { _player?.SetPause(true); } catch { /* native race */ }
        }

        private void TryStart()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) return;
            try
            {
                var player = new MediaPlayer(vlc) { EnableHardwareDecoding = true };
                _player = player;
                System.Threading.Interlocked.Increment(ref LivePlayers);
                _sink = new VlcFrameSink(player, () => _media, bmp => Source = bmp, () => { InvalidateVisual(); FrameShown?.Invoke(); });
                // EndReached re-play is WPF's fallback in case :input-repeat isn't honoured.
                if (Loops) player.EndReached += OnEndReached;
                if (StartOffset != null) player.Playing += OnFirstPlaying;
                _media = new Media(vlc, _path, FromType.FromPath);
                if (Loops) _media.AddOption(":input-repeat=65535"); // loop natively
                _media.AddOption(ConditioningControlPanel.Services.LibVlcSilence.NoAudioOption);           // muted; Mute would silence every player
                player.Play(_media);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InlineLoopVideo: failed to start {Path}", _path);
                Teardown();
            }
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            // LibVLC thread; Stop from inside its own event would deadlock, so re-play on the UI thread.
            var player = sender as MediaPlayer;
            Dispatcher.UIThread.Post(() =>
            {
                if (player == null || !ReferenceEquals(player, _player) || _media == null) return;
                try { player.Stop(); player.Play(_media); } catch { /* ignore */ }
            });
        }

        private void OnFirstPlaying(object? sender, EventArgs e)
        {
            // LibVLC thread; seek on the UI thread, only if this player is still ours (WPF's alive flag).
            var player = sender as MediaPlayer;
            Dispatcher.UIThread.Post(() =>
            {
                if (player == null || !ReferenceEquals(player, _player)) return;
                player.Playing -= OnFirstPlaying;
                try { player.Position = StartOffset!.Value; } catch { /* short clips may reject Position */ }
            });
        }

        /// <summary>Stop joins the decoder thread, so after it no callback touches the frame buffer
        /// (the BubbleCountWindow / MiniPlayerWindow teardown order).</summary>
        private void Teardown()
        {
            var player = _player;
            _player = null;
            if (player != null)
            {
                player.EndReached -= OnEndReached;
                player.Playing -= OnFirstPlaying;
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
                System.Threading.Interlocked.Decrement(ref LivePlayers);
            }
            try { _media?.Dispose(); } catch { }
            _media = null;
            Source = null;
            _sink?.Free();
            _sink = null;
        }
    }
}
