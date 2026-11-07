using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Services
{
    /// <summary>Everything the moving wallpaper knows about the Windows desktop: where the layer under the
    /// icons is, how a window gets there, how the desktop repaints after, and which monitors anyone can
    /// actually see. Wallpaper thread only, except <see cref="RepaintDesktop"/>.</summary>
    public sealed partial class DynamicWallpaperService
    {
        // ---- Win32 ----
        private const uint WM_SPAWN_WORKERW = 0x052C, SMTO_NORMAL = 0x0000;
        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        private static readonly int WS_POPUP = unchecked((int)0x80000000);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
        private const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowTitle);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);
        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        private IntPtr _progman, _workerW, _shellDefView;
        private bool _isRaised;
        private readonly List<Surface> _surfaces = new();
        private bool _holdStill;                   // on battery: the picture stays, the gifs stop

        /// <summary>Once a second: which monitors can anyone actually see, and is this PC on battery? A
        /// game or a maximised window in front means that monitor's wallpaper draws nothing at all, so the
        /// wallpaper never takes frames from what the player is doing.</summary>
        private void UpdateDesktopState()
        {
            try
            {
                _holdStill = WinForms.SystemInformation.PowerStatus.PowerLineStatus == WinForms.PowerLineStatus.Offline;

                var fg = GetForegroundWindow();
                RECT r = default;
                bool anyWindow = fg != IntPtr.Zero && !IsIconic(fg) && !IsShellWindow(fg) && GetWindowRect(fg, out r);
                bool zoomed = anyWindow && IsZoomed(fg);
                var fgMonitor = anyWindow ? MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST) : IntPtr.Zero;

                foreach (var s in _surfaces)
                {
                    var centre = new POINT { X = s.X + s.Width / 2, Y = s.Y + s.Height / 2 };
                    bool sameMonitor = anyWindow && MonitorFromPoint(centre, MONITOR_DEFAULTTONEAREST) == fgMonitor;
                    s.Covered = anyWindow && DynamicFrameBudget.Covers(
                        r.Left, r.Top, r.Right, r.Bottom, zoomed && sameMonitor, s.X, s.Y, s.Width, s.Height);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic desktop state"); }
        }

        /// <summary>The desktop and the taskbar are not "a window in front": Progman spans every monitor.</summary>
        private static bool IsShellWindow(IntPtr hwnd)
        {
            var name = new System.Text.StringBuilder(32);
            GetClassName(hwnd, name, name.Capacity);
            var cls = name.ToString();
            return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        }

        // ================================================================ desktop attach

        /// <summary>Resolve the desktop wallpaper layer. Detects the Win11 "raised desktop" (Progman has
        /// WS_EX_NOREDIRECTIONBITMAP), spawns the WorkerW with 0x052C(0xD,0x1), then finds the WorkerW and
        /// the SHELLDLL_DefView (the icons) to z-order against.</summary>
        private void SetupDesktopLayer()
        {
            _progman = FindWindow("Progman", null);
            _workerW = _shellDefView = IntPtr.Zero;
            if (_progman == IntPtr.Zero) { App.Logger?.Warning("[DynamicWallpaper] Progman window not found"); return; }

            _isRaised = (GetWindowLong(_progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;
            SendMessageTimeout(_progman, WM_SPAWN_WORKERW, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);

            // Classic layout: the WorkerW is the sibling AFTER the top-level window hosting DefView.
            IntPtr classicWorkerW = IntPtr.Zero, defView = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                var p = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (p != IntPtr.Zero)
                {
                    defView = p;
                    var w = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                    if (w != IntPtr.Zero) classicWorkerW = w;
                }
                return true;
            }, IntPtr.Zero);

            // Raised desktop: DefView + the wallpaper WorkerW are both children of Progman.
            IntPtr defViewUnderProgman = FindWindowEx(_progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr workerChildOfProgman = FindWindowEx(_progman, IntPtr.Zero, "WorkerW", null);
            if (_isRaised)
            {
                _workerW = workerChildOfProgman;
                _shellDefView = defViewUnderProgman != IntPtr.Zero ? defViewUnderProgman : defView;
            }
            else
            {
                _workerW = classicWorkerW != IntPtr.Zero ? classicWorkerW : workerChildOfProgman;
                _shellDefView = defView != IntPtr.Zero ? defView : defViewUnderProgman;
            }
            App.Logger?.Information("[DynamicWallpaper] Desktop layer: raised={Raised}, workerW={W}, icons={D}",
                _isRaised, _workerW != IntPtr.Zero, _shellDefView != IntPtr.Zero);
        }

        /// <summary>Raised desktop: WS_CHILD of Progman (NOT layered: DComp owns the pixels), placed on its
        /// monitor, z-ordered directly below the icons. Classic desktop: child of the WorkerW.
        /// Coordinates are relative to the parent, i.e. virtual-desktop origin removed.</summary>
        private void AttachToDesktop(IntPtr hwnd, Surface s, int vx, int vy)
        {
            try
            {
                if (_isRaised)
                {
                    int style = GetWindowLong(hwnd, GWL_STYLE);
                    SetWindowLong(hwnd, GWL_STYLE, (style & ~WS_POPUP) | WS_CHILD);
                    if (SetParent(hwnd, _progman) == IntPtr.Zero)
                        App.Logger?.Warning("[DynamicWallpaper] SetParent(Progman) failed (win32 {Err})", Marshal.GetLastWin32Error());
                    SetWindowPos(hwnd, IntPtr.Zero, s.X - vx, s.Y - vy, s.Width, s.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    SetWindowPos(hwnd, _shellDefView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                else
                {
                    if (SetParent(hwnd, _workerW) == IntPtr.Zero)
                        App.Logger?.Warning("[DynamicWallpaper] SetParent(WorkerW) failed (win32 {Err})", Marshal.GetLastWin32Error());
                    SetWindowPos(hwnd, IntPtr.Zero, s.X - vx, s.Y - vy, s.Width, s.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic attach to desktop"); }
        }

        /// <summary>Make every monitor repaint from the user's own wallpaper. Picture / solid colour: re-apply
        /// what is already set, through the write queue. Slideshow / Spotlight / unknown: never re-set
        /// anything (that would freeze the rotation), only ask the shell to redraw.</summary>
        private static void RepaintDesktop()
        {
            try
            {
                // A Static or Takeover wallpaper took over meanwhile: its own write repaints every monitor,
                // and re-applying what the registry said a moment ago could land over it.
                if (App.Wallpaper?.IsActive == true) return;

                int type = -1;
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers"))
                    if (key?.GetValue("BackgroundType") is int bt) type = bt;   // 0 picture, 1 solid colour, 2 slideshow

                if (type == 1)
                {
                    App.Wallpaper?.QueueDesktopRepaint("");
                    return;
                }
                if (type == 0)
                {
                    string? path;
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                        path = key?.GetValue("WallPaper") as string;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        App.Wallpaper?.QueueDesktopRepaint(path);
                        return;
                    }
                }
                var progman = FindWindow("Progman", null);
                if (progman != IntPtr.Zero)
                    RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "dynamic desktop refresh"); }
        }

        // ================================================================ types

        internal sealed class Tile : IDisposable
        {
            public readonly List<ID2D1Bitmap> Frames = new();
            public int[] Delays = Array.Empty<int>();
            public float Cx, Cy, AngleRad, ScaleX, ScaleY;
            public int BakeW, BakeH;
            public int Cur;
            public double Clock;

            public void Dispose()
            {
                foreach (var f in Frames) f.Dispose();
                Frames.Clear();
            }
        }

        private sealed class Surface
        {
            public HostForm? Form;
            public int X, Y, Width, Height;
            public IDXGISwapChain1? Swap;
            public ID2D1Bitmap1? Target;
            public IDCompositionTarget? DcTarget;
            public IDCompositionVisual? Visual;
            public bool NeedsDraw, Covered;
        }

        /// <summary>The per-monitor window DComp binds to. Shown without activation so starting a session
        /// never pulls focus away from CCP; reparented under the icons right after.</summary>
        private sealed class HostForm : WinForms.Form
        {
            protected override bool ShowWithoutActivation => true;

            protected override WinForms.CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE;
                    return cp;
                }
            }
        }
    }
}
