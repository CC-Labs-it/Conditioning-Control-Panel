using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Services.PrizeCounter;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// THE COUNTER SHEET OVER A GAME (owner 2026-11-17: "retire the 3D counter: doors inside the games open
/// the new 2D shop"). One per game window (<see cref="ChaosWebViewHost"/>: Back Room, Racing Thoughts,
/// chess, Rain's wardrobe), made on the first door press: the same <see cref="CounterSheetView"/> the
/// launcher and the panel carry, hung from a thin strip at the window's top edge.
///
/// <para>AIRSPACE. The game is a WebView2 child HWND over the whole window, so no WPF scrim can cover it.
/// While the sheet is open the game's last frame is shown as a still picture under the scrim
/// (<c>CapturePreviewAsync</c>), and the game's HWND is cut to an EMPTY window region: it keeps running
/// (each game pauses itself through its own counter door), but it draws nothing and takes no click, so
/// the scrim takes them. When the sheet is fully up again the region goes and the still is dropped.</para>
/// </summary>
internal sealed class GameCounterSheet
{
    private static readonly ConditionalWeakTable<Window, GameCounterSheet> ByWindow = new();

    /// <summary>The still is never waited on for longer than this: the game is cut out by then either way.</summary>
    private static readonly TimeSpan StillBudget = TimeSpan.FromMilliseconds(350);

    private readonly ChaosWebViewHost _game;
    private readonly Image _still;
    private int _freeze;
    private IntPtr _cutHwnd;

    public CounterSheetView View { get; }

    private GameCounterSheet(ChaosWebViewHost game, Window window, Grid grid, string from)
    {
        _game = game;
        _still = new Image { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Grid.SetRowSpan(_still, 99);
        Grid.SetColumnSpan(_still, 99);
        grid.Children.Add(_still);
        var strip = new Border
        {
            Height = CounterSheetRules.GameTopDip,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        Grid.SetRowSpan(strip, 99);
        Grid.SetColumnSpan(strip, 99);
        grid.Children.Add(strip);
        View = CounterSheetView.Mount(window, grid, strip, from);
        View.FocusDoorOnClose = false;
        // The strip above the scrim dims with it, so the whole game reads as under the sheet.
        strip.Background = View.ScrimLayer.Background;
        strip.SetBinding(UIElement.OpacityProperty, new Binding(nameof(UIElement.Opacity)) { Source = View.ScrimLayer });
        View.Retracted += Thaw;
    }

    /// <summary>This game window's sheet, made on first use. Null = the window cannot carry one (not up, not
    /// the host's own grid, no page): the door opens the counter window instead.</summary>
    public static GameCounterSheet? For(ChaosWebViewHost game, string from)
    {
        try
        {
            var window = game.Window;
            if (window == null || !window.IsVisible || window.Content is not Grid grid || game.WebView == null) return null;
            if (ByWindow.TryGetValue(window, out var have)) return have;
            var made = new GameCounterSheet(game, window, grid, from);
            ByWindow.Add(window, made);
            // Laid out now: the first press measures the strip and the window before the sheet opens.
            window.UpdateLayout();
            return made;
        }
        catch (Exception ex) { App.Logger?.Warning("PrizeSheet: no sheet over {From}: {E}", from, ex.Message); return null; }
    }

    /// <summary>The sheet just opened: the game's last frame goes under the scrim, then the game is cut out.</summary>
    public void Freeze()
    {
        int token = ++_freeze;
        var core = _game.WebView?.CoreWebView2;
        if (core == null) { Cut(token); return; }
        _ = CaptureAsync(core, token);
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = StillBudget };
        timer.Tick += (_, _) => { timer.Stop(); Cut(token); };
        timer.Start();
    }

    private async Task CaptureAsync(CoreWebView2 core, int token)
    {
        try
        {
            using var ms = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, ms).ConfigureAwait(true);
            if (token != _freeze || ms.Length == 0) return;
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            _still.Source = bmp;
            _still.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: game still failed: {E}", ex.Message); }
        Cut(token);
    }

    /// <summary>An empty region on the game's HWND: it draws nothing and takes no click.</summary>
    private void Cut(int token)
    {
        if (token != _freeze || _cutHwnd != IntPtr.Zero) return;
        try
        {
            var hwnd = _game.WebView?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            IntPtr none = CreateRectRgn(0, 0, 0, 0);
            if (SetWindowRgn(hwnd, none, true) == 0) { DeleteObject(none); return; }   // on success the window owns it
            _cutHwnd = hwnd;
        }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: game cut failed: {E}", ex.Message); }
    }

    /// <summary>The sheet is fully up: the game draws and takes input again, the still goes.</summary>
    private void Thaw()
    {
        ++_freeze;
        try { if (_cutHwnd != IntPtr.Zero) SetWindowRgn(_cutHwnd, IntPtr.Zero, true); }
        catch (Exception ex) { App.Logger?.Debug("PrizeSheet: game uncut failed: {E}", ex.Message); }
        _cutHwnd = IntPtr.Zero;
        _still.Source = null;
        _still.Visibility = Visibility.Collapsed;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
}
