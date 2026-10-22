using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// WPF VideoService.Controls.cs + Windows/VideoControlsOverlay.cs (5a3e335ff, 74dcf0445, 51287db40,
    /// e3bba7adf): the mute and close buttons in the top-right corner of every mandatory video window,
    /// shown once the clip is playing. WPF needed a separate owned window (LibVLC is a child HWND);
    /// here the clip is an Image, so the buttons sit inside each screen's window and go with it.
    ///   * CLOSE is the non-strict Esc dismiss minus the grace pause (<see cref="MandatoryVideoScheduler.End"/>).
    ///     Hidden under any lock (<see cref="MandatoryVideoControls.ShowClose"/>), taken down when a
    ///     Lockdown starts mid-video, and refused again on the press.
    ///   * MUTE silences this video run only (player volume 0, never the system mixer). Cleared when
    ///     the run ends, kept across an attention-check replay and the output heal.
    /// ponytail: chaosRunning is always false here - no Chaos run state exists on this head yet
    /// (ChaosOverlayWindow.OpenHubAt notes App.Chaos.IsRunning) and Down the Rabbit Hole is not hosted.
    /// </summary>
    internal sealed partial class MandatoryVideoOverlay
    {
        private volatile bool _userMuted;
        private bool _mutedAtClose;
        private object? _lockWatchOwner;

        internal bool UserMuted => _userMuted;
        /// <summary>The volume last handed to the player (LibVLC's own getter is -1 without an audio output).</summary>
        internal int PlayerVolume { get; private set; }

        private static bool LockdownActive => LockdownService.Current?.IsActive == true;
        private bool CloseAllowed => MandatoryVideoControls.ShowClose(_strict, LockdownActive, chaosRunning: false);
        private bool ControlsAllowed => MandatoryVideoControls.ShowMute(_strict, LockdownActive, chaosRunning: false);

        private void ApplyVolume(LibVLCSharp.Shared.MediaPlayer p)
        {
            var s = CoreSettings.Current;
            p.Volume = PlayerVolume = _userMuted ? 0 : MandatoryVideoScheduler.EffectiveVolume(s.MasterVolume, s.VideoVolume);
        }

        /// <summary>WPF ShowVideoControls: once the first frame is up, on every screen. UI thread.</summary>
        private void ShowVideoControls()
        {
            if (!Scheduler.IsPlaying || _closing || !ControlsAllowed) return;
            WatchForLocks();
            var close = CloseAllowed;
            foreach (var x in _surfaces)
            {
                x.Buttons.Close.IsVisible = close;
                x.Buttons.SetMuted(_userMuted);
                x.Buttons.Row.IsVisible = true;
            }
        }

        /// <summary>WPF ToggleUserMute: a press during a teardown does nothing.</summary>
        internal void ToggleUserMute()
        {
            if (!Scheduler.IsPlaying || _closing) return;
            _userMuted = !_userMuted;
            Log.Information("VideoService: mute button - video {State}", _userMuted ? "muted" : "unmuted");
            UpdateVolume();
            foreach (var x in _surfaces) x.Buttons.SetMuted(_userMuted);
        }

        /// <summary>WPF CloseVideoFromButton: the non-strict Esc dismiss, refused (and hidden) under a lock.</summary>
        internal void CloseFromButton()
        {
            if (!Scheduler.IsPlaying || _closing) return;
            if (!CloseAllowed)
            {
                Log.Information("VideoService: close button refused - the video is locked");
                HideCloseButtons();
                return;
            }
            Log.Information("VideoService: close button - dismissing the video (the Esc door)");
            Scheduler.End();
        }

        private void HideCloseButtons()
        {
            foreach (var x in _surfaces) x.Buttons.Close.IsVisible = false;
        }

        /// <summary>WPF WatchForLocks: a Lockdown that starts mid-video takes the X down. Subscribed once
        /// per Lockdown service, lazily (the service may come up after this overlay).</summary>
        private void WatchForLocks()
        {
            var lockdown = LockdownService.Current;
            if (lockdown == null || ReferenceEquals(_lockWatchOwner, lockdown)) return;
            _lockWatchOwner = lockdown;
            lockdown.LockdownActivated += () => Dispatcher.UIThread.Post(HideCloseButtons);
        }

        /// <summary>WPF VideoControlsOverlay: two 24 DIP black rounds with a light ring and a white glyph,
        /// 0.6 at rest, opaque on hover. Never focusable, so the window keeps its keys (the Esc door).</summary>
        internal sealed class VideoButtons
        {
            public readonly StackPanel Row;
            public readonly Button Mute, Close;
            public readonly IconGlyph MuteGlyph;

            public VideoButtons(Action onMute, Action onClose)
            {
                MuteGlyph = Glyph(IconKind.Speaker2);
                Mute = Round(MuteGlyph, onMute);
                Close = Round(Glyph(IconKind.Dismiss), onClose);
                Close.Margin = new Thickness(MandatoryVideoControls.GapDip, 0, 0, 0);
                ToolTip.SetTip(Close, Loc.Get("video_btn_close"));
                AutomationProperties.SetName(Close, Loc.Get("video_btn_close"));
                SetMuted(false);
                Row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, MandatoryVideoControls.MarginDip, MandatoryVideoControls.MarginDip, 0),
                    IsVisible = false,
                    Children = { Mute, Close },
                };
            }

            public void SetMuted(bool muted)
            {
                MuteGlyph.Kind = muted ? IconKind.SpeakerMute : IconKind.Speaker2;
                var tip = Loc.Get(muted ? "video_btn_unmute" : "video_btn_mute");
                ToolTip.SetTip(Mute, tip);
                AutomationProperties.SetName(Mute, tip);
            }

            private static IconGlyph Glyph(IconKind kind)
            {
                var g = IconGlyph.Of(kind, IconVariant.Regular, null, 12);
                g.Foreground = Brushes.White;
                g.IsHitTestVisible = false;
                g.HorizontalAlignment = HorizontalAlignment.Center;
                return g;
            }

            private static Button Round(Control glyph, Action onClick)
            {
                var size = MandatoryVideoControls.ButtonDip;
                var b = new Button
                {
                    Width = size,
                    Height = size,
                    Focusable = false,
                    Opacity = MandatoryVideoControls.RestOpacity,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    // The glyph lives in the template only: a Control has one parent (no Content).
                    Template = new FuncControlTemplate<Button>((_, _) => new Border
                    {
                        CornerRadius = new CornerRadius(size / 2),
                        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                        BorderThickness = new Thickness(1),
                        Child = glyph,
                    }),
                };
                b.PropertyChanged += (_, e) =>
                {
                    if (e.Property == InputElement.IsPointerOverProperty)
                        b.Opacity = b.IsPointerOver ? 1.0 : MandatoryVideoControls.RestOpacity;
                };
                b.Click += (_, e) =>
                {
                    e.Handled = true;
                    try { onClick(); }
                    catch (Exception ex) { Log.Debug("VideoControls: button handler threw - {Error}", ex.Message); }
                };
                return b;
            }
        }
    }
}
