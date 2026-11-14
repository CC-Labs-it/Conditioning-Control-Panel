using System;
using System.Diagnostics;
using System.ComponentModel;
using Avalonia.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Video.Floating;
namespace ConditioningControlPanel.Avalonia.Views.Overlays;
internal sealed class FloatingVideoFrame
{
    private readonly Window _window;
    private readonly Func<(PixelPoint Position, bool Down)?>? _nativePointer;
    private readonly FloatingVideoMotion _motion;
    private readonly HookProc _mouseHookProc;
    // Only the real cursor reader installs the system mouse hook; an injected pointer (tests) polls.
    private readonly bool _hookable;
    private IntPtr _mouseHook;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BeatLoop _beat;
    private readonly Func<double> _nowMs;
    private double _last, _lit, _breathSeconds;
    private bool _audioLit, _closed, _receivingNativeMoves;
    internal bool Running => _beat.IsRunning;
    internal double BreathSeconds => _breathSeconds;
    internal TimeSpan FrameInterval => _beat.Interval;
    private readonly Border? _frame;
    private readonly Func<bool> _audible;
    internal double MotionSpeed { get => _motion.Speed; set => _motion.Speed = value; }
    internal FloatingVideoFrame(Window window, Func<double>? nowMs = null, Border? frame = null, Func<bool>? audible = null,
        Func<(PixelPoint Position, bool Down)?>? nativePointer = null)
    {
        _window = window; _frame = frame;
        _mouseHookProc = OnNativeMouse;
        _nativePointer = nativePointer ?? (OperatingSystem.IsWindows() ? ReadNativePointer : null);
        _hookable = nativePointer == null && OperatingSystem.IsWindows();
        _audible = audible ?? (() => false);
        _nowMs = nowMs ?? (() => _clock.Elapsed.TotalMilliseconds);
        var b = window.Screens.ScreenFromWindow(window)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        _motion = new(window.Position.X, window.Position.Y, window.Width * window.RenderScaling,
            window.Height * window.RenderScaling, b.X, b.Y, b.Width, b.Height);
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.MonitorAt = (x, y) =>
        {
            var area = window.Screens.All.OrderBy(s => Distance(s.WorkingArea, x, y)).FirstOrDefault()?.WorkingArea;
            return area is { } a ? (a.X, a.Y, a.Width, a.Height) : null;
        };
        _beat = new BeatLoop(window, _ => Tick());
        window.PointerPressed += Press; window.PointerMoved += Move; window.PointerReleased += Release;
        window.PointerCaptureLost += (_, _) => { if (_motion.Held && _nativePointer == null) { _motion.Drop(); UpdateRate(); } };
        window.Screens.Changed += DisplaysChanged;
        var settings = CoreSettings.Current;
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(CoreSettings.Current.MotionLevel) or nameof(CoreSettings.Current.MandatoryVideoFloatingSpeedPercent)) Dispatcher.UIThread.Post(Refresh); };
        void Rebind() => Dispatcher.UIThread.Post(() => { if (_closed) return; settings.PropertyChanged -= changed; settings = CoreSettings.Current; settings.PropertyChanged += changed; Refresh(); });
        settings.PropertyChanged += changed;
        var service = CoreSettings.Service;
        if (service != null) service.CurrentReplaced += Rebind;
        window.Closed += (_, _) => { _closed = true; StopMouseHook(); _beat.Stop(); _motion.Drop(); window.Screens.Changed -= DisplaysChanged; settings.PropertyChanged -= changed; if (service != null) service.CurrentReplaced -= Rebind; };
        RefreshDesktop(); Refresh();
    }
    private static double Distance(PixelRect r, double x, double y)
    {
        var dx = Math.Max(r.X - x, Math.Max(0, x - r.Right));
        var dy = Math.Max(r.Y - y, Math.Max(0, y - r.Bottom));
        return dx * dx + dy * dy;
    }
    private void RefreshDesktop()
    {
        var areas = _window.Screens.All.Select(s => s.WorkingArea).ToArray();
        if (areas.Length == 0) return;
        var x = areas.Min(a => a.X); var y = areas.Min(a => a.Y);
        _motion.SetDesktop(x, y, areas.Max(a => a.Right) - x, areas.Max(a => a.Bottom) - y);
    }
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        RefreshDesktop();
        StopMouseHook(); _motion.Drop(); _motion.Rehome(); UpdateRate();
        Place();
    }
    private PixelPoint Pointer(PointerEventArgs e) => _window.PointToScreen(e.GetPosition(_window));
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed) return;
        if (CompositorDrag) { _window.BeginMoveDrag(e); return; }
        StartDrag(Pointer(e));
        if (_nativePointer == null) e.Pointer.Capture(_window);
    }
    internal void StartDrag(PixelPoint p)
    {
        _motion.Resync(_window.Position.X, _window.Position.Y,
            _window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling, rehome: true);
        _receivingNativeMoves = false;
        _motion.Press(p.X, p.Y, _nowMs());
        UpdateRate();
        if (_hookable)
        {
            StopMouseHook();
            _mouseHook = SetWindowsHookEx(14, _mouseHookProc, GetModuleHandle(null), 0);
            if (_mouseHook == IntPtr.Zero) Serilog.Log.Warning("Floating video mouse hook failed ({Code}); polling release", Marshal.GetLastWin32Error());
        }
        Refresh();
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (!_motion.Held || _nativePointer != null) return;
        var p = Pointer(e);
        _motion.Move(p.X, p.Y, _nowMs());
        _motion.Step(0.001);
        Place();
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (!_motion.Held || _nativePointer != null || e.InitialPressMouseButton != MouseButton.Left) return;
        var p = Pointer(e);
        var now = _nowMs();
        _motion.Move(p.X, p.Y, now);
        _motion.Release(now, CoreSettings.Current.MotionLevel);
        UpdateRate();
        e.Pointer.Capture(null);
        Place();
    }
    private void UpdateRate() => _beat.Interval = _motion.Held ? TimeSpan.Zero : TimeSpan.FromSeconds(1.0 / 30);
    internal void Refresh()
    {
        if (_closed) return;
        var audible = _audible();
        if (audible && !_audioLit) _breathSeconds = 0;
        _audioLit = audible;
        if (!_beat.IsRunning) _last = _nowMs() / 1000.0;
        if (NeedsClock) _beat.Start(); else Tick();
    }
    private bool NeedsClock => _motion.Active || (_audioLit ? _lit < 1 : _lit > 0)
        || (_audioLit && CoreSettings.Current.MotionLevel != ConditioningControlPanel.Models.MotionLevel.Off);
    internal void Tick()
    {
        if (_motion.Held && _nativePointer != null && _nativePointer() is { } pointer)
        {
            var ms = _nowMs();
            if (pointer.Down)
            {
                // With a hook, each native event already contributes its own timestamp.
                if (_mouseHook == IntPtr.Zero && !_receivingNativeMoves) _motion.Move(pointer.Position.X, pointer.Position.Y, ms);
            }
            else { FinishNativeDrag(pointer.Position, ms); Place(); }
        }
        var now = _nowMs() / 1000.0;
        var dt = Math.Clamp(now - _last, 0, 0.05);
        _lit = FloatingFrameLook.Approach(_lit, _audioLit, dt);
        if (_audioLit) _breathSeconds += dt;
        if (_frame != null)
        {
            var (r, g, b) = FloatingFrameLook.Border(FloatingFrameLook.AccentR, FloatingFrameLook.AccentG,
                FloatingFrameLook.AccentB, _lit, FloatingFrameLook.Breath(_breathSeconds, CoreSettings.Current.MotionLevel));
            var color = Color.FromRgb(r, g, b);
            if (_frame.BorderBrush is not SolidColorBrush brush || brush.Color != color) _frame.BorderBrush = new SolidColorBrush(color);
        }
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.Resize(_window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling);
        if (!_motion.Active)
            _motion.Resync(_window.Position.X, _window.Position.Y, _motion.W, _motion.H, rehome: true);
        _motion.Step(Math.Clamp(now - _last, 0, 0.05));
        _last = now;
        Place();
        if (!NeedsClock) _beat.Stop();
    }
    // A low-level hook follows the hand outside the window without capturing or consuming input.
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetTickCount();
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData { public NativePoint Point; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }
    private IntPtr OnNativeMouse(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && data != IntPtr.Zero && (int)message is 0x0200 or 0x0202 or 0x0205)
            {
                var mouse = Marshal.PtrToStructure<MouseHookData>(data);
                var point = new PixelPoint(mouse.Point.X, mouse.Point.Y);
                var ms = EventTime(mouse.Time, GetTickCount(), _nowMs());
                HandleNativeMouse((int)message, (int)message != 0x0200 && GetSystemMetrics(23) != 0, point, ms);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Floating video mouse hook failed"); }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    internal static double EventTime(uint eventTick, uint currentTick, double nowMs) =>
        nowMs - unchecked(currentTick - eventTick);
    internal void HandleNativeMouse(int message, bool swapped, PixelPoint point, double eventMs)
    {
        if (message != 0x0200) { HandleNativePointerUp(message, swapped, point, eventMs); return; }
        if (_nativePointer == null || !_motion.Held) return;
        _receivingNativeMoves = true;
        _motion.Move(point.X, point.Y, eventMs);
    }
    internal void HandleNativePointerUp(int message, bool swapped, PixelPoint point, double eventMs)
    {
        if (_nativePointer == null || !_motion.Held || message != (swapped ? 0x0205 : 0x0202)) return;
        FinishNativeDrag(point, eventMs);
        Place();
        Refresh();
    }
    private void FinishNativeDrag(PixelPoint point, double ms)
    {
        StopMouseHook();
        _motion.Move(point.X, point.Y, ms);
        _motion.Step(0.001);
        _motion.Release(ms, CoreSettings.Current.MotionLevel);
        UpdateRate();
    }
    private void StopMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private static (PixelPoint Position, bool Down)? ReadNativePointer()
    {
        if (!GetCursorPos(out var point)) return null;
        var key = PrimaryButtonKey(GetSystemMetrics(23) != 0); // SM_SWAPBUTTON, VK_RBUTTON, VK_LBUTTON
        return (new PixelPoint(point.X, point.Y), (GetAsyncKeyState(key) & 0x8000) != 0);
    }
    internal static int PrimaryButtonKey(bool swapped) => swapped ? 2 : 1;
    internal static bool UsesCompositorDrag(bool linux, IPlatformHandle? handle) =>
        linux && handle != null && handle.Handle != IntPtr.Zero && handle.HandleDescriptor != "XID";
    private bool CompositorDrag => UsesCompositorDrag(OperatingSystem.IsLinux(), _window.TryGetPlatformHandle());
    private void Place()
    {
        if (CompositorDrag) return;
        var position = new PixelPoint((int)Math.Round(_motion.X), (int)Math.Round(_motion.Y));
        if (_window.Position != position) _window.Position = position;
    }
}
