// WPF MainWindow.Awareness.cs UpdateAwarenessStatusIndicator -> SetAwarenessStatusPulse.
// Preserve the steady 8px indicator; only its sibling glow breathes on the window's 30fps beat.
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    private Window? _statusHost;
    private AppSettings? _statusSettings;
    private IDisposable? _statusVisibility;
    private BeatLoop? _statusBeat;

    private void InitializeStatusPulse()
    {
        AttachedToVisualTree += (_, _) =>
        {
            DetachStatusPulse();
            _statusHost = TopLevel.GetTopLevel(this) as Window;
            if (_statusHost is { } host)
            {
                host.Activated += StatusHostChanged;
                host.Deactivated += StatusHostChanged;
                host.PropertyChanged += StatusHostPropertyChanged;
                host.Closed += StatusHostClosed;
            }
            _statusSettings = CoreSettings.Current;
            _statusSettings.PropertyChanged += StatusSettingChanged;
            _statusVisibility = EffectiveVisibility.Watch(this, ApplyStatusPulse);
            AmbientFxCanvas.Env.MotionGateChanged += ApplyStatusPulse;
            CoreMods.ModChanged += StatusModChanged;
            ApplyStatusPulse();
        };
        DetachedFromVisualTree += (_, _) => DetachStatusPulse();
    }

    private void StatusHostChanged(object? sender, EventArgs e) => ApplyStatusPulse();
    private void StatusHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) ApplyStatusPulse();
    }
    private void StatusHostClosed(object? sender, EventArgs e) => DetachStatusPulse();
    private void StatusModChanged(object? sender, ModPackage mod) => DispatchStatusPulse(ApplyStatusPulse);
    private void StatusSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.KeywordTriggersEnabled))
            DispatchStatusPulse(() => UpdateStatusIndicator(CoreSettings.Current.KeywordTriggersEnabled));
    }
    private static void DispatchStatusPulse(Action apply)
    {
        if (Dispatcher.UIThread.CheckAccess()) apply();
        else Dispatcher.UIThread.Post(apply);
    }

    private void ApplyStatusPulse()
    {
        var tier = AmbientFxCanvas.Env.CurrentTier;
        bool run = _statusHost is { IsActive: true, WindowState: not WindowState.Minimized, PlatformImpl: not null }
            && IsEffectivelyVisible && CoreSettings.Current.KeywordTriggersEnabled
            && AmbientFxCanvas.Env.AllowAmbientLoops && AmbientFxCanvas.Env.AllowGlow(tier);
        if (!run) { StopStatusPulse(); return; }
        AwarenessStatusGlow.Background = AwarenessStatusDot.Fill;
        AwarenessStatusGlow.BoxShadow = new BoxShadows(new BoxShadow
        {
            Color = AmbientFxCanvas.Env.GlowColor, Blur = Math.Min(10, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)),
            OffsetX = 0, OffsetY = 0,
        });
        if (_statusBeat != null) return;
        _statusBeat = new BeatLoop(AwarenessStatusGlow,
            t => AwarenessStatusGlow.Opacity = Math.Clamp(0.22 + 0.73 * BeatLoop.Breath(t, 2.8), 0, 1));
        _statusBeat.Start();
    }

    private void StopStatusPulse()
    {
        _statusBeat?.Stop();
        _statusBeat = null;
        AwarenessStatusGlow.Opacity = 0;
        AwarenessStatusGlow.BoxShadow = default;
        // The dot's fill belongs to UpdateStatusIndicator, not to the animation.
        AwarenessStatusDot.Opacity = 1;
    }

    private void DetachStatusPulse()
    {
        StopStatusPulse();
        _statusVisibility?.Dispose(); _statusVisibility = null;
        if (_statusHost is { } host)
        {
            host.Activated -= StatusHostChanged;
            host.Deactivated -= StatusHostChanged;
            host.PropertyChanged -= StatusHostPropertyChanged;
            host.Closed -= StatusHostClosed;
        }
        _statusHost = null;
        if (_statusSettings != null) _statusSettings.PropertyChanged -= StatusSettingChanged;
        _statusSettings = null;
        AmbientFxCanvas.Env.MotionGateChanged -= ApplyStatusPulse;
        CoreMods.ModChanged -= StatusModChanged;
    }
}
