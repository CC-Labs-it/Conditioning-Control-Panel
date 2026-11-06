// WPF: the attached tube follows main LIVE while main is dragged (Window.LocationChanged fires on every
// step of a Windows move loop; AvatarTubeWindow.Windowing.cs hooks it). Avalonia's PositionChanged on X11
// comes from ConfigureNotify, and a reparenting WM such as openbox moves only its frame during an
// interactive move and sends the client its synthetic ConfigureNotify when the move ENDS. So the tube
// stood still for the whole drag, and stayed behind for good when the WM's move outlived the button
// (a quick press-drag-release that ended before the WM's grab began: the move then runs until the next
// click and the client is never told). Seen on openbox + picom (fix3, lane showquiet, ob5w.png).
//
// While main may be moving (from a pointer press on it until the button is up and main has been still
// for a moment), read main's real root origin from the X server every tick and dock to that. X11 only:
// on Windows and headless the reader returns null and nothing runs.

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
        internal const int FollowPollMs = 25;        // ~40 Hz, only while main may be moving
        internal const int FollowStillTicks = 8;     // button up and still for 200 ms: the move is over
        private static readonly TimeSpan FollowMaxRun = TimeSpan.FromSeconds(60);
        private DispatcherTimer? _followPoll;
        private PixelPoint _followStartPos, _followStartLive, _followLast;
        private int _followStill;
        private DateTime _followUntil;

        /// <summary>Main's client origin on the root as the X server has it now; null off X11. Test seam.</summary>
        internal Func<TopLevel, PixelPoint?> ReadRootOrigin = w => X11Overlay.TryGetRootOrigin(w, out var at) ? at : null;
        /// <summary>A mouse button is held desktop-wide; null when unknown. Test seam.</summary>
        internal Func<bool?> ReadButtonDown = () => X11Pointer.Read()?.Pressed;
        internal bool FollowPollRunning => _followPoll?.IsEnabled == true;

        private void OnParentPointerPressedForFollow(object? sender, PointerPressedEventArgs e) => StartFollowPoll();

        /// <summary>A press on main may be the start of a WM move (title bar, BeginMoveDrag): follow main's real
        /// position until it settles.</summary>
        internal void StartFollowPoll()
        {
            if (!_isAttached || _parentWindow is not { } p || ReadRootOrigin(p) is not { } live) return;
            _followStartPos = p.Position;
            _followStartLive = live;
            _followLast = live;
            _followStill = 0;
            _followUntil = DateTime.UtcNow + FollowMaxRun;
            _followPoll ??= new DispatcherTimer(TimeSpan.FromMilliseconds(FollowPollMs), DispatcherPriority.Input, (_, _) => FollowTick());
            _followPoll.Start();
        }

        internal void FollowTick()
        {
            if (!_isAttached || _parentWindow is not { } p || ReadRootOrigin(p) is not { } live || DateTime.UtcNow > _followUntil)
            {
                StopFollowPoll();
                return;
            }
            if (live != _followLast)
            {
                _followLast = live;
                _followStill = 0;
                // Main's Position as Avalonia will report it once the WM says so: same frame offset as at the press.
                UpdatePosition(new PixelPoint(_followStartPos.X + live.X - _followStartLive.X,
                                              _followStartPos.Y + live.Y - _followStartLive.Y));
            }
            else if (ReadButtonDown() != true && ++_followStill >= FollowStillTicks)
            {
                StopFollowPoll();
            }
        }

        private void StopFollowPoll() => _followPoll?.Stop();
    }
}
