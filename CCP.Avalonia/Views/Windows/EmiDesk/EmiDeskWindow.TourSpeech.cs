using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private DispatcherTimer? _tourSpeechTimer;
    private EmiChain? _tourSpeech;
    private int _tourSpeechFrame;
    private Border? _tourBubble;
    private TextBlock? _tourText;
    internal string? TourSpeechText => _tourBubble?.IsVisible == true ? _tourText?.Text : null;
    internal string? TourSpeechLine => _tourSpeech?.Seq[3].Bubble;
    internal int TourSpeechInterval => (int)(_tourSpeechTimer?.Interval.TotalMilliseconds ?? 0);

    // WPF EmiDeskWindow.xaml.cs:1071-1077 and Core EmiChains.MakeSay:519-531.
    internal void SayTourLine(string text, string face)
    {
        CancelChain();
        _tourSpeech = EmiChains.MakeSay(text, face, EmiChains.SayHoldMs(text));
        _tourSpeechFrame = 0;
        _tourSpeechTimer ??= new DispatcherTimer();
        _tourSpeechTimer.Tick -= AdvanceTourSpeech;
        _tourSpeechTimer.Tick += AdvanceTourSpeech;
        PaintTourSpeech();
    }

    private void AdvanceTourSpeech(object? sender, EventArgs e) => AdvanceTourSpeech();
    internal void AdvanceTourSpeech()
    {
        _tourSpeechTimer?.Stop();
        if (_tourSpeech == null) return;
        if (++_tourSpeechFrame >= _tourSpeech.Seq.Count) { StopTourSpeech(); return; }
        PaintTourSpeech();
    }

    private void PaintTourSpeech()
    {
        var frame = _tourSpeech!.Seq[_tourSpeechFrame];
        DrawFace(frame.Text);
        // WPF EmiDeskWindow.Bubble.cs:201-221,309-341: palette, font, width and crown anchor.
        if (_tourBubble == null)
        {
            _tourText = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#FF69B4")),
                FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            _tourBubble = new Border { Background = new SolidColorBrush(Color.Parse("#181832")),
                BorderBrush = _tourText.Foreground, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8), Child = _tourText, IsHitTestVisible = false };
            _bubbleCanvas.Children.Add(_tourBubble);
        }
        _tourText!.Text = frame.Bubble;
        _tourBubble.IsVisible = !string.IsNullOrEmpty(frame.Bubble);
        _tourText.FontSize = Math.Max(10, Math.Round(11 * BodyWidth / 220));
        _tourText.LineHeight = Math.Round(_tourText.FontSize * 1.4);
        _tourBubble.MaxWidth = Math.Clamp(BodyWidth * 1.5, 220, 380);
        _tourBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _tourBubble.DesiredSize;
        double rightLeft = OverlayPadX + BodyWidth * .58;
        double flippedLeft = OverlayPadX + BodyWidth * .42 - size.Width;
        // WPF Bubble.cs:368-403: convert through the window, then use the shared clip guard.
        double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            lo = this.PointToClient(new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y)).X;
            hi = this.PointToClient(new PixelPoint(screen.WorkingArea.Right, screen.WorkingArea.Y)).X;
        }
        var (left, _) = EmiBubblePlacement.Resolve(rightLeft, flippedLeft, size.Width, false, Width, lo, hi);
        Canvas.SetLeft(_tourBubble, Math.Round(left));
        Canvas.SetTop(_tourBubble, Math.Round(Math.Max(2, OverlayPad + BodyWidth * BodyAspect * .04 - size.Height)));
        _tourSpeechTimer!.Interval = TimeSpan.FromMilliseconds(frame.Ms);
        _tourSpeechTimer.Start();
    }

    partial void OnBubbleTextCore(string? text)
    {
        if (string.IsNullOrEmpty(text)) StopTourSpeech();
    }

    internal void StopTourSpeech()
    {
        _tourSpeechTimer?.Stop();
        _tourSpeech = null;
        if (_tourBubble != null) _tourBubble.IsVisible = false;
    }
}
