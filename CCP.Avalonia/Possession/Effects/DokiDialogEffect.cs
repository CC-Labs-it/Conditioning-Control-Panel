// WPF: Services/Possession/Effects/DokiDialogEffect.cs:14-297.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class DokiDialogEffect : PossessionEffectBase
{
    private Window? _dialog;
    private DispatcherTimer? _timer;
    private TextBlock? _status;
    private Border? _bar;
    internal Window? Dialog => _dialog;
    public override string Id => "dokidialog";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool IsBig => true;
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
        var title = Loc.Get(ctx.Rng.Next(2) == 0 ? "possession_doki_title_1" : "possession_doki_title_2");
        var caption = new Border { Background = crimson, Padding = new Thickness(10, 5),
            Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold } };
        var face = new Image { Width = 76, Height = 110, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Source = Views.AvatarTube.AvatarTubeWindow.Live?.FindControl<Image>("ImgAvatar")?.Source };
        var faceArea = new Grid { Width = 76, Height = 110 };
        faceArea.Children.Add(new TextBlock { Text = "♥", Foreground = crimson, FontSize = 48,
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
        var started = DateTime.UtcNow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => StepProgress((DateTime.UtcNow - started).TotalMilliseconds);
        _timer.Start();
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
        _timer?.Stop(); _timer = null;
        _status = null; _bar = null;
        var dialog = _dialog; _dialog = null;
        dialog?.Close();
    }

    protected override Task UndoCoreAsync(TimeSpan duration) { CloseDialog(); return Task.CompletedTask; }
}
