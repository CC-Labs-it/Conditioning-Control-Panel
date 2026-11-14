using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// ccp-bugs #1548 (7.2.1, Windows build 26220): the launcher grew 800 -> 1000 px on open and then
/// painted only the first row of tiles; the rest stayed clickable but invisible. After a game and
/// back, no tile painted at all. Every show sets each tile to Opacity 0 and hands it to the entrance
/// stagger, so a stagger clock that never finishes (or a frame the compositor never repaints after
/// the grow) leaves a hit-testable tile nobody can see.
///
/// <para>The settle pass runs once the stagger should be long over: any tile still faded is put at
/// full opacity with its rise cleared, and the window is asked to repaint. It logs what it found
/// (how many tiles were still faded, the render tier and mode), so the next report says whether the
/// stagger stalled or the paint was lost. It never runs while the Play beat has dimmed the others.</para>
///
/// <para>The screenshot later showed the WHOLE client unpainted, so the real fix is the render
/// target work in LauncherWindow.Render.cs; this pass also runs its paint probe and logs monitor,
/// DPI and cloak state.</para>
/// </summary>
public partial class LauncherWindow
{
    /// <summary>The stagger ends at 6 x 40 ms + 260 ms; this leaves room for a slow first frame.</summary>
    private const int TileSettleMs = 1200;

    private DispatcherTimer? _tileSettle;
    private string _tileSettleWhy = "show";
    private bool _tilesDimmed;

    private void ScheduleTileSettle(string why)
    {
        try
        {
            if (_tileSettle == null)
            {
                _tileSettle = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(TileSettleMs),
                };
                _tileSettle.Tick += TileSettle_Tick;
            }
            _tileSettleWhy = why;
            _tileSettle.Stop();
            _tileSettle.Start();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tile settle schedule failed"); }
    }

    private void TileSettle_Tick(object? sender, EventArgs e)
    {
        _tileSettle?.Stop();
        try
        {
            if (!IsVisible || _tilesDimmed) return;
            int faded = 0;
            foreach (var tile in _tiles)
            {
                if (tile.Opacity >= 0.99) continue;
                faded++;
                tile.BeginAnimation(UIElement.OpacityProperty, null);
                tile.Opacity = 1;
                if (tile.RenderTransform is TransformGroup g)
                    foreach (var t in g.Children)
                        if (t is TranslateTransform slide)
                        {
                            slide.BeginAnimation(TranslateTransform.YProperty, null);
                            slide.Y = 0;
                        }
            }

            GamesGrid.InvalidateVisual();
            RootGrid.InvalidateVisual();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwAllChildren | RdwUpdateNow | RdwFrame);

            string mode = "?";
            try { mode = HwndSource.FromHwnd(hwnd)?.CompositionTarget?.RenderMode.ToString() ?? "?"; } catch { }
            // #1548: the screenshot showed the whole client unpainted, not faded tiles. Read the
            // screen behind the window and say where it is, so the next report proves which.
            string probe;
            try { probe = ProbePaint(hwnd); }
            catch (Exception ex) { probe = "probe failed (" + ex.GetType().Name + ")"; }
            bool blank = probe.Contains("BLANK", StringComparison.Ordinal);
            if (faded > 0 || blank)
                Log.Information("[Launcher] tile settle ({Why}): {Faded} of {Count} tiles were still faded, forced visible (tier {Tier}, {Mode}, {W:0}x{H:0} DIP, {Cloak}, last kick {Kick}; {Monitor}; {Probe})",
                    _tileSettleWhy, faded, _tiles.Count, RenderCapability.Tier >> 16, mode, ActualWidth, ActualHeight,
                    CloakState(hwnd), _lastKick, DescribeMonitor(), probe);
            else
                Log.Debug("[Launcher] tile settle ({Why}): {Count} tiles visible (tier {Tier}, {Mode}, {W:0}x{H:0} DIP, {Cloak}, last kick {Kick}; {Monitor}; {Probe})",
                    _tileSettleWhy, _tiles.Count, RenderCapability.Tier >> 16, mode, ActualWidth, ActualHeight,
                    CloakState(hwnd), _lastKick, DescribeMonitor(), probe);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] tile settle failed"); }
    }

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
}
