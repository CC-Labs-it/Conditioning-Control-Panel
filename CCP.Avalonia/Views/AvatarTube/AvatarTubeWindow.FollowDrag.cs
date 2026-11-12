// WPF: the attached tube follows main LIVE while main is dragged (Window.LocationChanged fires on every
// step of a Windows move loop; AvatarTubeWindow.Windowing.cs hooks it). Avalonia's PositionChanged on X11
// comes from ConfigureNotify, and a reparenting WM such as openbox moves only its frame during an
// interactive move and sends the client its synthetic ConfigureNotify when the move ENDS. So the tube
// stood still for the whole drag. Worse, a quick press-drag-release that ends before the WM's grab
// begins leaves openbox in the move: main keeps following the pointer with no button held until the
// next click, and the client hears nothing. Seen on openbox + picom (fix3, lane showquiet, ob5w.png).
//
// So from a pointer press on main, read main's real root origin from the X server and dock to it:
// every 25 ms while main moves or the button is held; once the button is up and main has been still for
// 200 ms, every 250 ms (a WM move may still be running) until the WM reports a position (the move is
// over) or 15 s pass without movement. X11 only: on Windows and headless the reader returns null and
// nothing runs.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        internal const int FollowPollMs = 25;         // ~40 Hz while main moves or the button is held
        internal const int FollowIdlePollMs = 250;    // a WM move may still be running: look 4x a second
        internal const int FollowStillTicks = 8;      // button up and still for 200 ms
        private static readonly TimeSpan FollowQuietLimit = TimeSpan.FromSeconds(15);
        private DispatcherTimer? _followPoll;
        private PixelPoint _followStartPos, _followStartLive, _followLast;
        private int _followStill;
        private bool _followReported;
        private DateTime _followUntil;

        /// <summary>Main's client origin on the root as the X server has it now; null off X11. Test seam.</summary>
        internal Func<TopLevel, PixelPoint?> ReadRootOrigin = w => X11Overlay.TryGetRootOrigin(w, out var at) ? at : null;
        /// <summary>A mouse button is held desktop-wide; null when unknown. Test seam.</summary>
        internal Func<bool?> ReadButtonDown = () => X11Pointer.Read()?.Pressed;
        /// <summary>Clock for the quiet limit. Test seam.</summary>
        internal Func<DateTime> FollowClock = () => DateTime.UtcNow;
        internal bool FollowPollRunning => _followPoll?.IsEnabled == true;
        internal bool FollowPollIdle => FollowPollRunning && _followPoll!.Interval.TotalMilliseconds >= FollowIdlePollMs;

        private void OnParentPointerPressedForFollow(object? sender, PointerPressedEventArgs e) => StartFollowPoll();

        /// <summary>A press on main may be the start of a WM move (title bar, BeginMoveDrag): follow main's real
        /// position until the move is reported or main stays put.</summary>
        internal void StartFollowPoll()
        {
            if (!_isAttached || _parentWindow is not { } p || ReadRootOrigin(p) is not { } live) return;
            _followStartPos = p.Position;
            _followStartLive = live;
            _followLast = live;
            _followStill = 0;
            _followReported = false;
            _followUntil = FollowClock() + FollowQuietLimit;
            _followPoll ??= new DispatcherTimer(DispatcherPriority.Input);
            _followPoll.Tick -= OnFollowTimer;
            _followPoll.Tick += OnFollowTimer;
            _followPoll.Interval = TimeSpan.FromMilliseconds(FollowPollMs);
            _followPoll.Start();
        }

        private void OnFollowTimer(object? sender, EventArgs e) => FollowTick();

        /// <summary>Main's position was reported by the WM (Avalonia PositionChanged): the move it belonged to is over.</summary>
        private void NoteParentPositionReported()
        {
            if (FollowPollRunning) _followReported = true;
        }

        internal void FollowTick()
        {
            if (!_isAttached || _parentWindow is not { } p || ReadRootOrigin(p) is not { } live || FollowClock() > _followUntil)
            {
                StopFollowPoll();
                return;
            }
            if (live != _followLast)
            {
                _followLast = live;
                _followStill = 0;
                _followUntil = FollowClock() + FollowQuietLimit;
                SetFollowInterval(FollowPollMs);
                // Main's Position as Avalonia will report it once the WM says so: same frame offset as at the press.
                UpdatePosition(new PixelPoint(_followStartPos.X + live.X - _followStartLive.X,
                                              _followStartPos.Y + live.Y - _followStartLive.Y));
                return;
            }
            if (ReadButtonDown() == true || ++_followStill < FollowStillTicks) return;
            if (_followReported) StopFollowPoll();         // the WM said where main ended: nothing left to follow
            else SetFollowInterval(FollowIdlePollMs);      // a move may still be running without a button
        }

        private void SetFollowInterval(int ms)
        {
            if (_followPoll != null && (int)_followPoll.Interval.TotalMilliseconds != ms) _followPoll.Interval = TimeSpan.FromMilliseconds(ms);
        }

        private void StopFollowPoll() => _followPoll?.Stop();
    }
}
