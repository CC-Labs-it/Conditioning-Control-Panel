using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private DispatcherTimer? _promotedClearTimer;
    private string? _durationRememberedFor;

    // WPF keeps discovery hints separate from asynchronous transport status updates.
    private void ShowHint(string text, double? autoClearSeconds)
    {
        ClearHint();
        var hint = this.FindControl<TextBlock>("TxtHint")!;
        hint.Text = text;
        hint.IsVisible = true;
        if (autoClearSeconds is > 0)
        {
            _promotedClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoClearSeconds.Value) };
            _promotedClearTimer.Tick += (_, _) => ClearHint();
            _promotedClearTimer.Start();
        }
    }

    private void ClearHint()
    {
        _promotedClearTimer?.Stop();
        _promotedClearTimer = null;
        var hint = this.FindControl<TextBlock>("TxtHint")!;
        hint.Text = "";
        hint.IsVisible = false;
    }

    // WPF publishes the first positive length for each loaded local file, never a URL.
    private void RememberDurationOnce(string? mediaPath, double seconds)
    {
        if (string.IsNullOrEmpty(mediaPath) || seconds <= 0 || !double.IsFinite(seconds)) return;
        if (string.Equals(_durationRememberedFor, mediaPath, StringComparison.OrdinalIgnoreCase)) return;
        _durationRememberedFor = mediaPath;
        try { MediaDurationCache.Remember(mediaPath, seconds); }
        catch (Exception ex) { Log.Debug(ex, "EnhancementPlayer: duration cache publication failed"); }
    }
}
