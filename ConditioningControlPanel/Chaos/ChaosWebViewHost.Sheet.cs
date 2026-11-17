using System;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel;

/// <summary>
/// SHEET mode (<see cref="Options.SheetOwner"/> + <see cref="Options.SheetBounds"/>): a borderless page
/// that hangs from another window of ours (the Prize Counter sheet under a wallet bar). It is a WPF
/// OWNED window, so it rides above its owner, has no taskbar button and goes when the owner closes;
/// it never takes the topmost pulse <see cref="BringToFront"/> gives the game windows (that would lift
/// it, and not its owner, over the user's other apps). It sits where <see cref="Options.SheetBounds"/>
/// says (screen pixels), follows the owner when it moves or resizes, and reports
/// <see cref="Options.OnSheetLost"/> when it loses activation or its owner hides, minimizes or closes.
/// The host closes nothing itself: what "lost" means is the caller's.
/// </summary>
internal sealed partial class ChaosWebViewHost
{
    /// <summary>True for a host built in sheet mode.</summary>
    public bool IsSheet => _opts.SheetOwner != null && _opts.SheetBounds != null;

    private Window? _sheetOwner;

    private void LayoutSheet()
    {
        if (_window == null || _opts.SheetOwner is not { } owner) return;
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.ShowInTaskbar = false;
        try { _window.Owner = owner; } catch (Exception ex) { Diag.Swallowed(ex, "sheet owner"); }
        // A first guess in DIPs so the frame never flashes at a default spot; the pixel-exact move
        // follows on SourceInitialized (before the window is visible) and again on Loaded.
        if (_opts.SheetBounds?.Invoke() is { } px)
        {
            double s = OwnerScale(owner);
            _window.Left = px.X / s; _window.Top = px.Y / s;
            _window.Width = Math.Max(1, px.Width / s); _window.Height = Math.Max(1, px.Height / s);
        }
        _window.SourceInitialized += (_, _) => PlaceSheet();
        _window.Loaded += (_, _) => PlaceSheet();
        _window.Deactivated += OnSheetDeactivated;

        _sheetOwner = owner;
        owner.LocationChanged += OnSheetOwnerMoved;
        owner.SizeChanged += OnSheetOwnerResized;
        owner.StateChanged += OnSheetOwnerStateChanged;
        owner.IsVisibleChanged += OnSheetOwnerVisibleChanged;
        owner.Closed += OnSheetOwnerClosed;
    }

    private static double OwnerScale(Window owner)
    {
        try
        {
            var m = PresentationSource.FromVisual(owner)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            return m > 0 ? m : 1.0;
        }
        catch (Exception ex) { Diag.Swallowed(ex); return 1.0; }
    }

    /// <summary>Move the frame to the bounds the caller measures now (pixels, no z-order, no activation).
    /// Bounds that no longer fit (the owner shrank below the minimum) count as the sheet being lost.</summary>
    private void PlaceSheet()
    {
        if (_disposed || _window == null || _opts.SheetBounds == null) return;
        try
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (_opts.SheetBounds() is not { } r) { RaiseSheetLost("no-room"); return; }
            SetWindowPos(hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.PlaceSheet: {E}", _opts.LogTag, ex.Message); }
    }

    private void OnSheetDeactivated(object? sender, EventArgs e) => RaiseSheetLost("deactivated");
    private void OnSheetOwnerMoved(object? sender, EventArgs e) => PlaceSheet();
    private void OnSheetOwnerResized(object? sender, SizeChangedEventArgs e) => PlaceSheet();
    private void OnSheetOwnerClosed(object? sender, EventArgs e) => RaiseSheetLost("owner-closed");

    private void OnSheetOwnerStateChanged(object? sender, EventArgs e)
    {
        if (_sheetOwner?.WindowState == WindowState.Minimized) RaiseSheetLost("owner-minimized");
        else PlaceSheet();
    }

    private void OnSheetOwnerVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) RaiseSheetLost("owner-hidden");
    }

    private void RaiseSheetLost(string why)
    {
        if (_disposed) return;
        try { _opts.OnSheetLost?.Invoke(why); }
        catch (Exception ex) { App.Logger?.Debug("{Tag}.OnSheetLost: {E}", _opts.LogTag, ex.Message); }
    }

    private void DetachSheet()
    {
        try { if (_window != null) _window.Deactivated -= OnSheetDeactivated; } catch (Exception ex) { Diag.Swallowed(ex); }
        var owner = _sheetOwner;
        _sheetOwner = null;
        if (owner == null) return;
        try
        {
            owner.LocationChanged -= OnSheetOwnerMoved;
            owner.SizeChanged -= OnSheetOwnerResized;
            owner.StateChanged -= OnSheetOwnerStateChanged;
            owner.IsVisibleChanged -= OnSheetOwnerVisibleChanged;
            owner.Closed -= OnSheetOwnerClosed;
        }
        catch (Exception ex) { Diag.Swallowed(ex); }
    }
}
