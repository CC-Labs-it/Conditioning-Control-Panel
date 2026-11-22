using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Gives a typed-answer card (lock card, bubble count answer) the real keyboard foreground.
    ///
    /// <para>ccp-bugs #1698: a plain <c>SetForegroundWindow</c> (and WPF <c>Activate</c>, which is the
    /// same call) fails SILENTLY when another process owns the foreground, for example the player was
    /// typing in Discord when the card fired. The card is topmost, so it still draws on top and looks
    /// active, but every key keeps going to the other app until the card is clicked. This borrows the
    /// foreground thread's input state for one call (the <c>AttachThreadInput</c> method that
    /// <c>AvatarTubeWindow.ForceForegroundWindow</c> and <c>ChaosWebViewHost</c> already use), and
    /// <see cref="RecheckSoon"/> looks again once the show has settled, retakes on a miss and logs it,
    /// so the next report shows the miss directly. No key is ever injected: panic and Escape are untouched.</para>
    /// </summary>
    internal static class ForegroundGrab
    {
        internal enum Step
        {
            /// <summary>The target already owns the foreground: nothing to do.</summary>
            AlreadyOwned,
            /// <summary>The foreground is ours (or nobody's): a plain SetForegroundWindow is allowed.</summary>
            Direct,
            /// <summary>Another thread owns it: plain call first, borrow its input state if that misses.</summary>
            BorrowInput,
        }

        /// <summary>Pure decision half, unit tested (no Win32).</summary>
        internal static Step Plan(IntPtr foreground, IntPtr target, uint foregroundThread, uint ourThread)
        {
            if (target != IntPtr.Zero && foreground == target) return Step.AlreadyOwned;
            if (foregroundThread == 0 || foregroundThread == ourThread) return Step.Direct;
            return Step.BorrowInput;
        }

        /// <summary>Take the foreground for <paramref name="hwnd"/>. Returns true when it owns it after the call.</summary>
        public static bool Take(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                var fg = GetForegroundWindow();
                uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
                uint ours = GetCurrentThreadId();

                switch (Plan(fg, hwnd, fgThread, ours))
                {
                    case Step.AlreadyOwned:
                        return true;
                    case Step.Direct:
                        SetForegroundWindow(hwnd);
                        break;
                    case Step.BorrowInput:
                        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;
                        // Always detach in finally: threads left attached wedge keyboard input desktop-wide.
                        bool attached = false;
                        try
                        {
                            attached = AttachThreadInput(ours, fgThread, true);
                            BringWindowToTop(hwnd);
                            SetForegroundWindow(hwnd);
                        }
                        finally
                        {
                            if (attached) { try { AttachThreadInput(ours, fgThread, false); } catch { } }
                        }
                        break;
                }
                return GetForegroundWindow() == hwnd;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ForegroundGrab: take failed: {E}", ex.Message);
                return false;
            }
        }

        /// <summary>True when <paramref name="hwnd"/> owns the foreground right now.</summary>
        public static bool Owns(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd; }
            catch { return false; }
        }

        /// <summary>
        /// Once the show has settled (ContextIdle: after layout and pending input), check the card really
        /// owns the keyboard; if not, take it again and re-run <paramref name="refocus"/>. One retry, one
        /// log line on a miss. <paramref name="stillWanted"/> is re-read at that moment so a card that was
        /// solved, closed or panicked in the meantime is left alone.
        /// </summary>
        public static void RecheckSoon(Window window, Func<IntPtr> hwnd, Func<bool> stillWanted, Action refocus, string who)
        {
            try
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!stillWanted()) return;
                        var h = hwnd();
                        if (h == IntPtr.Zero || Owns(h)) return;
                        bool ok = Take(h);
                        try { window.Activate(); } catch { }
                        refocus();
                        App.Logger?.Warning("{Who}: shown without the keyboard foreground, retake {Result}",
                            who, ok ? "worked" : "failed (a click on the card gives it the keyboard)");
                    }
                    catch (Exception ex) { App.Logger?.Debug("ForegroundGrab: recheck failed: {E}", ex.Message); }
                }), DispatcherPriority.ContextIdle);
            }
            catch (Exception ex) { App.Logger?.Debug("ForegroundGrab: recheck not queued: {E}", ex.Message); }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }
}
