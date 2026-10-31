using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private DispatcherTimer? _speechTimer;
    private EmiChain? _speech;
    private int _speechIndex;
    private Action? _speechDone;
    private Border? _bubble;
    private TextBlock? _bubbleText;
    private Polygon? _bubbleTail;

    internal string? BubbleText => _bubble?.IsVisible == true ? _bubbleText?.Text : null;
    internal TimeSpan SpeechHold => _speechTimer?.Interval ?? TimeSpan.Zero;

    // WPF EmiBookWindow.xaml.cs:976 and EmiDeskWindow.Bubble.cs:503.
    // Offers and the service's NoteEmiSpoke bus are still part B.
    internal void SpeakBookMargin(EmiBookCard card)
    {
        if (PresentationActive || string.IsNullOrWhiteSpace(card.MarginEn)) return;
        EmiLineEngine.Instance.Ack("book.margin." + card.Id);
        Say(card.MarginEn, string.IsNullOrEmpty(card.MarginFace) ? "^_^" : card.MarginFace);
    }

    private void OnSpeechTick(object? sender, EventArgs e) => AdvanceSpeech();

    // Called by the real dispatcher timer; tests advance the same boundary without wall-clock sleeps.
    internal void AdvanceSpeech()
    {
        if (_speech == null) return;
        _speechTimer?.Stop();
        if (++_speechIndex < _speech.Seq.Count) { PaintSpeechFrame(); return; }
        var done = _speechDone;
        StopSpeech();
        DrawFace(EmiChains.RestFace);
        done?.Invoke();
    }

    private void PaintSpeechFrame()
    {
        var frame = _speech!.Seq[_speechIndex];
        DrawFace(frame.Text);
        if (frame.SetsBubble) OnBubbleTextCore(frame.Bubble);
        _speechTimer!.Interval = TimeSpan.FromMilliseconds(frame.Ms);
        _speechTimer.Start();
    }

    private void StopSpeech()
    {
        _speechTimer?.Stop();
        _speech = null;
        _speechDone = null;
        OnBubbleTextCore(null);
    }

    // WPF EmiDeskWindow.Bubble.cs:140,197,276,291. No voice or offer consumer yet.
    partial void OnBubbleTextCore(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (_bubble != null) _bubble.IsVisible = false;
            if (_bubbleTail != null) _bubbleTail.IsVisible = false;
            return;
        }
        if (_bubble == null)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            var fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
            _bubbleText = new TextBlock { Foreground = ink, FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"),
                TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            _bubble = new Border { Background = fill, BorderBrush = ink, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8), IsHitTestVisible = false, Child = _bubbleText };
            _bubbleTail = new Polygon { Fill = fill, Stroke = ink, StrokeThickness = 1, IsHitTestVisible = false };
            BubbleHost.Children.Add(_bubbleTail);
            BubbleHost.Children.Add(_bubble);
            _bubble.SizeChanged += (_, _) => LayoutBubble();
            Resized += (_, _) => LayoutBubble();
            // Owner movement precedes book follow-placement in the event invocation list.
            Moved += (_, _) => Dispatcher.UIThread.Post(LayoutBubble, DispatcherPriority.Background);
        }
        _bubbleText!.Text = text;
        _bubble.IsVisible = true;
        _bubbleTail!.IsVisible = true;
        LayoutBubble();
    }

    // WPF EmiDeskWindow.Bubble.cs:303,392,475. All screen arithmetic uses local DIPs.
    internal void LayoutBubble()
    {
        if (_closingForGood || _bubble?.IsVisible != true || _bubbleText == null) return;
        double bw = BodyWidth;
        double fs = Math.Max(10, Math.Round(11 * bw / 220));
        _bubbleText.FontSize = fs;
        _bubbleText.LineHeight = Math.Round(fs * 1.4);
        _bubble.MaxWidth = Math.Max(220, Math.Min(380, bw * 1.5));
        _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = Math.Max(_bubble.DesiredSize.Width, _bubble.Bounds.Width);
        double h = Math.Max(_bubble.DesiredSize.Height, _bubble.Bounds.Height);
        double workLeft = double.NegativeInfinity, workRight = double.PositiveInfinity;
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null && IsVisible)
        {
            var area = screen.WorkingArea;
            workLeft = this.PointToClient(new PixelPoint(area.X, area.Y)).X;
            workRight = this.PointToClient(new PixelPoint(area.Right, area.Y)).X;
        }
        var (left, flip) = EmiBubblePlacement.Resolve(OverlayPadX + bw * .58,
            OverlayPadX + bw * .42 - w, w, _book?.SideOfHer > 0, Width, workLeft, workRight);
        double top = Math.Max(2, OverlayPad + bw * BodyAspect * .04 - h);
        Canvas.SetLeft(_bubble, Math.Round(left));
        Canvas.SetTop(_bubble, Math.Round(top));
        _bubbleTail!.Points = flip
            ? new Points { new(0, 0), new(10, 0), new(10, 10) }
            : new Points { new(10, 0), new(0, 0), new(0, 10) };
        Canvas.SetLeft(_bubbleTail, Math.Round(flip ? left + w - 10 : left));
        Canvas.SetTop(_bubbleTail, Math.Round(top + h - 1));
    }
}
