using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private IDisposable? _quickRecalRegistration;
    private DispatcherTimer? _quickRecalBindingWatch;
    private (bool Enabled, bool PanicOn, string Panic, string Pause)? _quickRecalBinding;
    private bool _quickRecalBusy, _quickRecalClosed;

    // Native seam: headless tests never register a shortcut on the real desktop.
    internal Func<Window, Action, IDisposable?> RegisterQuickRecal = Win32QuickRecalHotkey.Register;
    internal Func<WebcamQuickRecalWindow, Window?, Task> ShowQuickRecal = ShowQuickRecalDialog;
    internal bool QuickRecalPlatformSupported = OperatingSystem.IsWindows();
    internal bool QuickRecalHotkeyArmed => _quickRecalRegistration != null;

    /// <summary>WPF MainWindow.xaml.cs:1455-1459: a hidden or minimized shell must not own the dialog.
    /// No owner means a plain Show(), not ShowDialogSafe(null), which would adopt any visible window,
    /// a minimized shell included.</summary>
    internal static Task ShowQuickRecalDialog(WebcamQuickRecalWindow dialog, Window? owner)
    {
        if (owner != null) return dialog.ShowDialogSafe(owner);
        var closed = new TaskCompletionSource();
        dialog.Closed += (_, _) => closed.TrySetResult();
        dialog.Show();
        return closed.Task;
    }

    /// <summary>WPF MainWindow.ApplyGlobalQuickRecalHotkey. Desktop startup only; render/tests do not arm keys.</summary>
    internal void StartQuickRecalHotkey()
    {
        if (!QuickRecalPlatformSupported || _quickRecalBindingWatch != null) return;
        ApplyQuickRecalHotkey();
        // ponytail: observe preset/remote binding changes on one slow timer instead of adding
        // callbacks to every setting writer. Invocation rechecks immediately, including during this gap.
        _quickRecalBindingWatch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => ApplyQuickRecalHotkey());
        _quickRecalBindingWatch.Start();
        Closed += (_, _) => StopQuickRecalHotkey();
    }

    internal void ApplyQuickRecalHotkey()
    {
        var s = CoreSettings.Current;
        var binding = (s.WebcamQuickRecalHotkeyEnabled, s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        if (_quickRecalBinding == binding) return;
        _quickRecalBinding = binding;
        _quickRecalRegistration?.Dispose();
        _quickRecalRegistration = null;
        if (!s.WebcamQuickRecalHotkeyEnabled || _quickRecalClosed) return;
        if (PanicPolicy.FindHookClash("G", PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
        {
            Log.Warning("Quick Recal Ctrl+Alt+G not armed: shares its base key with {Binding} ({Key})", clash.Name, clash.Key);
            return;
        }
        try
        {
            _quickRecalRegistration = RegisterQuickRecal(this,
                () => Dispatcher.UIThread.Post(async () => await OpenQuickRecalFromHotkeyAsync()));
        }
        catch (Exception ex) { Log.Warning(ex, "Quick Recal hotkey registration failed"); }
    }

    internal void StopQuickRecalHotkey()
    {
        _quickRecalClosed = true;
        _quickRecalBindingWatch?.Stop();
        _quickRecalBindingWatch = null;
        _quickRecalRegistration?.Dispose();
        _quickRecalRegistration = null;
    }

    /// <summary>WPF OpenQuickRecalFromHotkey: silent refusal, no consent prompt, no stacked calibration,
    /// preserve a running tracker and stop only a camera this invocation started.</summary>
    internal async Task OpenQuickRecalFromHotkeyAsync()
    {
        var s = CoreSettings.Current;
        if (_quickRecalClosed || _quickRecalBusy || !s.WebcamQuickRecalHotkeyEnabled) return;
        if (PanicPolicy.FindHookClash("G", PanicPolicy.HookBoundBaseKeys(s)) != null)
        {
            _quickRecalRegistration?.Dispose();
            _quickRecalRegistration = null;
            _quickRecalBinding = null;
            return;
        }
        var tracker = WebcamTracker.Instance;
        if (!WebcamConsent.IsCurrent(s) || tracker.Calibration == null) return;
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .Any(w => w is WebcamQuickRecalWindow or WebcamCalibrationWindow) == true) return;
        _quickRecalBusy = true;
        bool startedHere = false;
        try
        {
            if (!tracker.IsRunning)
            {
                if (!await tracker.StartAsync()) return;
                startedHere = true;
            }
            if (_quickRecalClosed || !tracker.IsRunning || !WebcamConsent.IsCurrent(CoreSettings.Current)) return;
            var dialog = new WebcamQuickRecalWindow();
            await ShowQuickRecal(dialog, IsVisible && WindowState != WindowState.Minimized ? this : null);
        }
        catch (Exception ex) { Log.Warning(ex, "Quick Recal hotkey flow failed"); }
        finally
        {
            if (startedHere) await tracker.StopAsync();
            _quickRecalBusy = false;
        }
    }
}
