using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private readonly HashSet<Key> _heldKeys = new();
    private int? _volumeBeforeMute;
    private bool _muteChanging;
    private BeatLoop? _escHoldTimer;   // WPF: 50 ms DispatcherTimer; here the shared beat (elapsed-time logic unchanged)
    private DateTime _escHoldStart, _lastEscSignal;
    private bool _escConsumed;

    private void InitializeInteractions()
    {
        // Avalonia has no IsRepeat: track down/up pairs, clearing on focus loss.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (FocusManager?.GetFocusedElement() is TextBox) return;
            var repeat = !_heldKeys.Add(e.Key);
            if (HandlePlayerKeyDown(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), repeat))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) =>
        {
            _heldKeys.Remove(e.Key);
            if (e.Key != Key.Escape || FocusManager?.GetFocusedElement() is TextBox) return;
            HandleEscapeUp();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => { _heldKeys.Clear(); CancelEscHold(); };
        _miniTimelineCanvas.PointerExited += (_, _) => this.FindControl<Border>("MiniHoverLabel")!.IsVisible = false;
    }

    private bool HandlePlayerKeyDown(Key key, bool shift, bool repeat)
    {
        switch (key)
        {
            case Key.Space:
                if (!repeat) BtnPlayPause_Click();
                return true;
            case Key.Left: SeekRelative(shift ? -30 : -5); return true;
            case Key.Right: SeekRelative(shift ? 30 : 5); return true;
            case Key.Up: _sliderVolume.Value = Math.Clamp(_sliderVolume.Value + 5, 0, 100); return true;
            case Key.Down: _sliderVolume.Value = Math.Clamp(_sliderVolume.Value - 5, 0, 100); return true;
            case Key.M:
                if (!repeat) ToggleMute();
                return true;
            case Key.Escape:
                _lastEscSignal = DateTime.UtcNow;
                if (!repeat) HandleEscapeDown();
                return true;
            // ponytail: F and browser-focused keys require the native fullscreen/message bridge.
            default: return false;
        }
    }

    private void SeekRelative(double seconds)
    {
        if (_isVideoMode && _videoNavigated)
        {
            var target = Math.Max(0, _currentSec + seconds);
            if (_durationSec > 0) target = Math.Min(target, _durationSec);
            _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke(
                "l.currentTime=" + target.ToString("0.###", CultureInfo.InvariantCulture) + ";"));
        }
        else if (_localAudio is { DurationSeconds: > 0 } audio)
            SeekAudio(Math.Clamp(audio.PositionSeconds + seconds, 0, audio.DurationSeconds));
    }

    private void ToggleMute()
    {
        try
        {
            _muteChanging = true;
            if (_volumeBeforeMute is int restore)
            {
                _volumeBeforeMute = null;
                _sliderVolume.Value = restore;
            }
            else
            {
                _volumeBeforeMute = (int)Math.Round(_sliderVolume.Value);
                _sliderVolume.Value = 0;
            }
        }
        finally { _muteChanging = false; }
    }

    private static bool IsUnderStrictLock() =>
        LockdownService.Current?.IsActive == true || CoreSettings.Current.StrictLockEnabled;

    private bool IsSessionActive() => _localAudio != null || _videoNavigated;

    private void HandleEscapeDown()
    {
        if (_changePopup.IsOpen)
        {
            _changePopup.IsOpen = false;
            _escConsumed = true;
            return;
        }
        if (_escHoldTimer != null) return;
        _escConsumed = false;
        if (IsUnderStrictLock() || !IsSessionActive()) return;
        _escHoldStart = DateTime.UtcNow;
        _escHoldTimer = new BeatLoop(this, t => { if (t > 0) EscHoldTimer_Tick(null, EventArgs.Empty); });
        _escHoldTimer.Start();
        ShowHoldEscHint(0);
    }

    private void EscHoldTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastEscSignal).TotalSeconds > 1.5) { CancelEscHold(); return; }
        var held = (now - _escHoldStart).TotalSeconds;
        ShowHoldEscHint(Math.Min(1, held / 3));
        if (held < 3) return;
        CancelEscHold();
        _escConsumed = true;
        BtnStop_Click();
        _txtStatus.Text = Loc.Get("deeper_player_status_escape_stopped");
    }

    private void HandleEscapeUp()
    {
        CancelEscHold();
        if (_escConsumed) { _escConsumed = false; return; }
        if (IsUnderStrictLock() || IsSessionActive()) return;
        Close();
    }

    private void CancelEscHold()
    {
        if (_escHoldTimer != null)
        {
            var loop = _escHoldTimer;
            _escHoldTimer = null;
            loop.Stop();
        }
        this.FindControl<StackPanel>("HoldEscHint")!.IsVisible = false;
    }

    private void ShowHoldEscHint(double fraction)
    {
        this.FindControl<ProgressBar>("HoldEscProgress")!.Value = Math.Clamp(fraction, 0, 1);
        this.FindControl<StackPanel>("HoldEscHint")!.IsVisible = true;
    }

    private void UpdateMiniHoverLabel(double x)
    {
        var label = this.FindControl<Border>("MiniHoverLabel")!;
        var width = _miniTimelineCanvas.Bounds.Width;
        if (width <= 0 || _miniTotalSeconds <= 0 || _miniEnhancement == null)
        {
            label.IsVisible = false;
            return;
        }
        this.FindControl<TextBlock>("TxtMiniHoverTime")!.Text = FormatTime(Math.Clamp(x / width, 0, 1) * _miniTotalSeconds);
        label.IsVisible = true;
        var labelWidth = label.Bounds.Width > 0 ? label.Bounds.Width : 36;
        label.Margin = new global::Avalonia.Thickness(Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, width - labelWidth)), 2, 0, 0);
    }
}
