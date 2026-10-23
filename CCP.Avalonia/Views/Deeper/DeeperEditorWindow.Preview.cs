using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class DeeperEditorWindow
{
    internal static bool IsAnyPreviewFullscreen() => s_open.Any(e => e._previewFullscreenWindow != null);

    internal static void StopPreviewFullscreenForPanic()
    {
        foreach (var editor in s_open.ToArray()) editor._previewBridge.LeaveFullscreen();
    }

    // WPF DeeperEditorWindow.xaml.cs:754: detach before rehosting; refuse unknown parents.
    private static bool TryDetachFromUiParent(Control child)
    {
        switch (child.Parent)
        {
            case Panel panel: panel.Children.Remove(child); break;
            case Decorator decorator when ReferenceEquals(decorator.Child, child): decorator.Child = null; break;
            case ContentControl content when ReferenceEquals(content.Content, child): content.Content = null; break;
            case null: break;
            default: return false;
        }
        return child.Parent == null && child.GetVisualParent() == null;
    }

    private void EnterPreviewFullscreen()
    {
        if (_fsTransitionInFlight || _isPreviewFullscreen || !IsVisible || !BrowserPreview.IsVisible || _playbackDisposed) return;
        _fsTransitionInFlight = true;
        Window? built = null;
        try
        {
            using var reparent = BrowserPreview.BeginReparenting();
            if (!TryDetachFromUiParent(BrowserPreview)) { _previewBridge.LeaveFullscreen(); return; }
            var screen = Screens.ScreenFromVisual(this);
            built = new Window
            {
                WindowDecorations = WindowDecorations.None, CanResize = false,
                ShowInTaskbar = false, Topmost = true, Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = screen?.Bounds.Position ?? Position,
                Width = 400, Height = 300, Content = BrowserPreview
            };
            built.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Escape or Key.F11) { _previewBridge.LeaveFullscreen(); e.Handled = true; }
            };
            // Detach while the native fullscreen host still exists, including Alt+F4/system close.
            built.Closing += (_, _) =>
            {
                using var reparent = BrowserPreview.BeginReparenting();
                built.Content = null;
                SafeRestoreBrowserPreviewToHost();
            };
            built.Closed += (_, _) =>
            {
                if (ReferenceEquals(_previewFullscreenWindow, built)) _previewFullscreenWindow = null;
                using var reparent = BrowserPreview.BeginReparenting();
                built.Content = null;
                _isPreviewFullscreen = false;
                SafeRestoreBrowserPreviewToHost();
                _previewBridge.LeaveFullscreen();
            };
            _previewFullscreenWindow = built;
            built.Show(this);
            built.WindowState = WindowState.FullScreen;
            _isPreviewFullscreen = true;
            _ = BrowserPreview.InvokeScriptAsync("window._ccpForcedFs=true;");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DeeperEditor: preview fullscreen failed");
            if (built != null) { built.Content = null; built.Close(); }
            _previewFullscreenWindow = null;
            _isPreviewFullscreen = false;
            SafeRestoreBrowserPreviewToHost();
            _previewBridge.LeaveFullscreen();
        }
        finally { _fsTransitionInFlight = false; }
    }

    private void SafeRestoreBrowserPreviewToHost()
    {
        if (BrowserPreview.Parent == null && BrowserPreview.GetVisualParent() == null) PreviewHost.Children.Insert(0, BrowserPreview);
        BrowserPreview.Focus();
    }

    private void ExitPreviewFullscreen()
    {
        if (_fsTransitionInFlight || (!_isPreviewFullscreen && _previewFullscreenWindow == null)) return;
        _fsTransitionInFlight = true;
        try
        {
            using var reparent = BrowserPreview.BeginReparenting();
            _isPreviewFullscreen = false;
            var fullscreen = _previewFullscreenWindow;
            _previewFullscreenWindow = null;
            if (fullscreen != null) { fullscreen.Content = null; fullscreen.Close(); }
            SafeRestoreBrowserPreviewToHost();
        }
        finally { _fsTransitionInFlight = false; }
    }
}
