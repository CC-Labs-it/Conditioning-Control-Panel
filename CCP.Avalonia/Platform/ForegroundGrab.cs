using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/UI/ForegroundGrab.cs:24-111. Only HWNDs use the native path.
internal static class ForegroundGrab
{
    internal enum Step { AlreadyOwned, Direct, BorrowInput }
    internal static Step Plan(IntPtr foreground, IntPtr target, uint foregroundThread, uint ourThread) =>
        target != IntPtr.Zero && foreground == target ? Step.AlreadyOwned :
        foregroundThread == 0 || foregroundThread == ourThread ? Step.Direct : Step.BorrowInput;

    private static int _generation;
    internal static int Generation => Volatile.Read(ref _generation);
    internal static void CancelPending() => Interlocked.Increment(ref _generation);

    internal static IntPtr Handle(Window window) => OperatingSystem.IsWindows() &&
        window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } h ? h.Handle : IntPtr.Zero;

    internal static bool Owns(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return false;
        try { return GetForegroundWindow() == hwnd; } catch { return false; }
    }

    internal static bool Take(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero) return false;
        try
        {
            var foreground = GetForegroundWindow();
            uint thread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
            uint ours = GetCurrentThreadId();
            switch (Plan(foreground, hwnd, thread, ours))
            {
                case Step.AlreadyOwned: return true;
                case Step.Direct: SetForegroundWindow(hwnd); break;
                case Step.BorrowInput:
                    if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
                    bool attached = false;
                    try
                    {
                        attached = AttachThreadInput(ours, thread, true);
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
                    }
                    finally { if (attached) AttachThreadInput(ours, thread, false); }
                    break;
            }
            return GetForegroundWindow() == hwnd;
        }
        catch (Exception ex) { Log.Debug(ex, "ForegroundGrab: take failed"); return false; }
    }

    // Kept separate from the dispatcher so cancellation and the single retry can be tested.
    internal static void Recheck(int generation, Func<bool> wanted, Func<IntPtr> handle,
        Func<IntPtr, bool> owns, Func<IntPtr, bool> take, Action refocus, string who)
    {
        try
        {
            if (generation != Generation || !wanted()) return;
            var hwnd = handle();
            if (hwnd == IntPtr.Zero || owns(hwnd)) return;
            bool ok = take(hwnd);
            refocus();
            Log.Warning("{Who}: shown without the keyboard foreground, retake {Result}", who, ok ? "worked" : "failed (a click on the card gives it the keyboard)");
        }
        catch (Exception ex) { Log.Debug(ex, "ForegroundGrab: recheck failed"); }
    }

    internal static void RecheckSoon(Window window, Func<bool> wanted, Action refocus, string who)
    {
        if (!OperatingSystem.IsWindows()) return;
        int generation = Generation;
        Dispatcher.UIThread.Post(() => Recheck(generation, wanted, () => Handle(window), Owns, Take,
            () => { window.Activate(); refocus(); }, who), DispatcherPriority.ContextIdle);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
