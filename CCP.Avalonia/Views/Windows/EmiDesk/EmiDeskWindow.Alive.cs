using System;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private DispatcherTimer? _gazeTimer;
    internal Func<PixelPoint?> AlivePointer = () => OperatingSystem.IsWindows()
        ? Win32Overlay.CursorPosition() : X11Pointer.Read()?.At;
    internal double AliveGazeX => _gazeShift.X;
    internal bool? AliveWaylandOverride, AliveX11Override;
    internal Func<(PixelPoint At, bool OverWindow)?> AliveXPointer = () =>
    {
        var result = X11Pointer.Read(out bool overWindow);
        return result is null ? null : (result.Value.At, overWindow);
    };
    private Point? _surfacePointer;

    // Wayland exposes pointer coordinates only for surfaces owned by this client. Store them in
    // body DIPs, not screen coordinates: a native Wayland window has no reliable global origin.
    private void WireGazePointer()
    {
        PointerEntered += OnGazePointer;
        AddHandler(PointerMovedEvent, OnGazePointer, RoutingStrategies.Bubble, handledEventsToo: true);
        PointerExited += (_, e) => { if (e.Pointer.Type == PointerType.Mouse) _surfacePointer = null; };
    }

    private void OnGazePointer(object? sender, PointerEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Mouse) _surfacePointer = e.GetPosition(_bodyRoot);
    }


    private void StartGaze()
    {
        StopGaze();
        if (PresentationActive || _closingForGood) return;
        _gazeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(EmiAlive.PollMs) };
        _gazeTimer.Tick += OnGazeTick;
        _gazeTimer.Start();
    }

    private void StopGaze()
    {
        if (_gazeTimer != null)
        {
            _gazeTimer.Stop();
            _gazeTimer.Tick -= OnGazeTick;
            _gazeTimer = null;
        }
        _surfacePointer = null;
        _gazeShift.X = _gazeShift.Y = 0;
    }

    private void OnGazeTick(object? sender, EventArgs e) => PollGaze();

    internal void PollGaze()
    {
        if (!IsVisible || _closingForGood) return;
        bool wayland = AliveWaylandOverride ?? LeashHead.WaylandSession;
        Point? cursor = null;
        double bodyX = 0, bodyY = 0, width = 0, height = 0;
        if (wayland && _surfacePointer is { } local)
        {
            cursor = local;
            bodyX = bodyY = 0;
            width = _bodyWidth;
            height = _bodyWidth * BodyAspect;
        }
        else
        {
            PixelPoint? pointer;
            if (wayland)
            {
                // XWayland can retain the last position after focus leaves X. It is useful only
                // with an X11 desk and a confirmed X surface under the pointer.
                bool x11 = AliveX11Override ?? X11Overlay.BackendOf(TryGetPlatformHandle()) == OverlayBackend.X11;
                var sample = x11 ? AliveXPointer() : null;
                pointer = sample is { OverWindow: true } ? sample.Value.At : null;
            }
            else pointer = AlivePointer();
            if (pointer is { } position)
            {
                double s = DipScale;
                var body = BodyScreenRect;
                cursor = new Point(position.X / s, position.Y / s);
                bodyX = body.X / s; bodyY = body.Y / s;
                width = body.Width / s; height = body.Height / s;
            }
        }
        if (cursor is not null && (width <= 0 || height <= 0)) return;
        // A refused read has no gaze target. Ease home so a lost Wayland pointer cannot freeze it.
        double x = 0, y = 0;
        if (cursor is { } at && AmbientFxCanvas.Env.Level == MotionLevel.Full && !Busy() && !_dragging && !_resizing)
            (x, y) = EmiAlive.GazeTarget(at.X, at.Y, bodyX, bodyY, width, height, _bodyWidth);
        _gazeShift.X = EmiAlive.Ease(_gazeShift.X, x, EmiAlive.GazeEasePerPoll);
        _gazeShift.Y = EmiAlive.Ease(_gazeShift.Y, y, EmiAlive.GazeEasePerPoll);
        if (Math.Abs(_gazeShift.X - x) + Math.Abs(_gazeShift.Y - y) < .02)
        {
            _gazeShift.X = x;
            _gazeShift.Y = y;
        }
    }
}
