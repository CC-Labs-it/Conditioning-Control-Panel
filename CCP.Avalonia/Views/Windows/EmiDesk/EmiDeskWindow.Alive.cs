using System;
using Avalonia;
using Avalonia.Threading;
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
        _gazeShift.X = _gazeShift.Y = 0;
    }

    private void OnGazeTick(object? sender, EventArgs e) => PollGaze();

    internal void PollGaze()
    {
        if (!IsVisible || _closingForGood) return;
        var pointer = AlivePointer();
        if (pointer is null) return;
        double s = DipScale;
        var body = BodyScreenRect;
        if (body.Width <= 0 || body.Height <= 0) return;
        double x = 0, y = 0;
        if (AmbientFxCanvas.Env.Level == MotionLevel.Full && !Busy() && !_dragging && !_resizing)
            (x, y) = EmiAlive.GazeTarget(pointer.Value.X / s, pointer.Value.Y / s,
                body.X / s, body.Y / s, body.Width / s, body.Height / s, _bodyWidth);
        _gazeShift.X = EmiAlive.Ease(_gazeShift.X, x, EmiAlive.GazeEasePerPoll);
        _gazeShift.Y = EmiAlive.Ease(_gazeShift.Y, y, EmiAlive.GazeEasePerPoll);
        if (Math.Abs(_gazeShift.X - x) + Math.Abs(_gazeShift.Y - y) < .02)
        {
            _gazeShift.X = x;
            _gazeShift.Y = y;
        }
    }
}
