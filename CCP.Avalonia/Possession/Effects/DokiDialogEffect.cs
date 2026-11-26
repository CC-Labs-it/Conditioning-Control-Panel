// WPF: Services/Possession/Effects/DokiDialogEffect.cs:14-297.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class DokiDialogEffect : PossessionEffectBase
{
    private static readonly HashSet<DokiDialogEffect> Fading = new();
    private IFrameSource? _fadeFrames;
    private IDisposable? _fadeTimeout;
    private TaskCompletionSource? _faded;
    private TimeSpan? _fadeStart;
    private double _fadeMs;
    private Window? _dialog;
    private BeatLoop? _progress;
    private DispatcherTimer? _failsafe;
    private TextBlock? _status;
    private Border? _bar;
    internal Window? Dialog => _dialog;
    public override string Id => "dokidialog";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
    protected override string NameOverride => Loc.Get("possession_doki_window");
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Rung >= MinRung && ctx.Intensity >= MinIntensity && _dialog == null &&
        ctx.Host is IAvaloniaPossessionHost { PossessionWindow: { IsVisible: true } };

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (!CanApplyCore(ctx, target)) return Task.CompletedTask;
        var owner = ((IAvaloniaPossessionHost)ctx.Host).PossessionWindow!;
        var crimson = new SolidColorBrush(Color.Parse("#DC143C"));
        var ink = new SolidColorBrush(Color.Parse("#FFE3DA"));
        var dim = new SolidColorBrush(Color.Parse("#C09A92"));
        var trackColor = new SolidColorBrush(Color.Parse("#2A1414"));
        bool bambi = string.Equals(RewritePools.ActiveModId, BuiltInMods.BambiSleepId, StringComparison.OrdinalIgnoreCase);
        int choice = ctx.Rng.Next(bambi ? 3 : 2);
        var title = Loc.Get(bambi && choice == 0 ? "possession_doki_title_bambi" :
            choice == (bambi ? 1 : 0) ? "possession_doki_title_1" : "possession_doki_title_2");
        var caption = new Border { Background = crimson, Padding = new Thickness(10, 5),
            Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold } };
        var face = new Image { Width = 76, Height = 110, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Source = Views.AvatarTube.AvatarTubeWindow.Live?.FindControl<Image>("ImgAvatar")?.Source };
        var faceArea = new Grid { Width = 76, Height = 110 };
        faceArea.Children.Add(new IconGlyph { Kind = IconKind.Heart, Variant = IconVariant.Filled, Size = 48, Foreground = crimson,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        faceArea.Children.Add(face);
        var status = new TextBlock { Text = Loc.Get("possession_doki_progress") + " 0%", Foreground = dim,
            Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
        var bar = new Border { Width = 0, Height = 8, Background = crimson, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Width = 260, Height = 10, Background = trackColor, BorderBrush = crimson,
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Child = bar };
        var ok = new Button { Content = Loc.Get("possession_doki_ok"), Width = 74, Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, Background = trackColor, Foreground = ink, BorderBrush = crimson };
        ok.Click += (_, _) => CloseDialog();
        var column = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        column.Children.Add(new TextBlock { Text = Loc.Get("possession_doki_message"), Foreground = ink,
            FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        column.Children.Add(status); column.Children.Add(track); column.Children.Add(ok);
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 12) };
        body.Children.Add(faceArea); body.Children.Add(column);
        var content = new StackPanel(); content.Children.Add(caption); content.Children.Add(body);
        var dialog = _dialog = new Window { Width = 440, Height = 214, CanResize = false,
            SystemDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = title,
            Background = new SolidColorBrush(Color.Parse("#1A0A0A")),
            Content = new Border { BorderBrush = crimson, BorderThickness = new Thickness(2), Child = content } };
        dialog.Closed += (_, _) => { if (ReferenceEquals(_dialog, dialog)) CloseDialog(); };
        dialog.Show(owner);
        _status = status; _bar = bar;
        _fadeFrames = ((IAvaloniaPossessionHost)ctx.Host).PossessionFrames;
        // The reveal rides the window's shared beat (parked while hidden); the close failsafe is wall
        // clock, so a dialog whose frames stop still goes away (WPF DokiDialogEffect failsafe timer).
        _progress = new BeatLoop(bar, seconds => StepProgress(seconds * 1000));
        _progress.Start();
        _failsafe = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _failsafe.Tick += (_, _) => CloseDialog();
        _failsafe.Start();
        return Task.CompletedTask;
    }

    // The same elapsed clock drives the progress reveal and the independent close failsafe.
    internal void StepProgress(double elapsed)
    {
        if (_status == null || _bar == null) return;
        var t = Math.Clamp(elapsed / 2200, 0, 1);
        var eased = t * t * (3 - 2 * t);
        _bar.Width = 260 * eased;
        _status.Text = t >= 1 ? Loc.Get("possession_doki_done") : Loc.Get("possession_doki_progress") + " " + (int)Math.Round(eased * 100) + "%";
        if (elapsed >= 6000) CloseDialog();
    }

    private void CloseDialog()
    {
        _fadeFrames?.Unsubscribe(FadeFrame); _fadeFrames = null;
        _fadeTimeout?.Dispose(); _fadeTimeout = null;
        _fadeStart = null; Fading.Remove(this);
        var faded = _faded; _faded = null; faded?.TrySetResult();
        _progress?.Stop(); _progress = null;
        _failsafe?.Stop(); _failsafe = null;
        _status = null; _bar = null;
        var dialog = _dialog; _dialog = null;
        dialog?.Close();
    }

    // WPF DokiDialogEffect.cs:281-295: cubic fade, clamped to 120-320 ms.
    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        if (_dialog == null || duration <= TimeSpan.Zero || _fadeFrames == null)
        {
            CloseDialog();
            return Task.CompletedTask;
        }
        _fadeMs = Math.Clamp(duration.TotalMilliseconds, 120, 320);
        _fadeStart = null;
        var faded = _faded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Fading.Add(this);
        _fadeFrames.Subscribe(FadeFrame);
        _fadeTimeout = DispatcherTimer.RunOnce(() => { if (ReferenceEquals(_faded, faded)) CloseDialog(); },
            TimeSpan.FromMilliseconds(_fadeMs + 100));
        return faded.Task;
    }

    private void FadeFrame(TimeSpan now)
    {
        if (_dialog == null) return;
        _fadeStart ??= now;
        var t = Math.Clamp((now - _fadeStart.Value).TotalMilliseconds / _fadeMs, 0, 1);
        var ease = t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        _dialog.Opacity = 1 - ease;
        if (t >= 1) CloseDialog();
    }

    internal static void StopFades(IPossessionHost? host = null)
    {
        foreach (var effect in Fading.ToArray())
            if (host == null || ReferenceEquals(effect.Context?.Host, host)) effect.CloseDialog();
    }
}
