using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private Border? _speechBubble;
    private TextBlock? _speechText;
    private Polygon? _speechTail;
    private DispatcherTimer? _speechTimer;
    private IReadOnlyList<EmiFrame>? _speechFrames;
    private int _speechIndex;
    private Action? _speechDone;
    private bool _bubbleRelayoutPending;

    // ponytail: the general offer/hold channel and vox/body-pose hooks remain in
    // wire-win-emi-book-c. Book margins have no chain/hold and cannot draw an offer.
    public void SpeakLine(LineDraw? line)
    {
        if (PresentationActive || line is null) return;
        EmiLineEngine.Instance.Ack(line.Id);
        if (string.IsNullOrWhiteSpace(line.Text)) { DrawFace(line.Face); return; }
        Say(line.Text, string.IsNullOrWhiteSpace(line.Face) ? "^_^" : line.Face);
    }

    private void StartSpeech(string? line, string face, Action? done)
    {
        if (PresentationActive || _closingForGood) return;
        CancelChain();
        StopIdleBeats();
        _speechFrames = EmiSpeech.MakeSay(line, face, EmiSpeech.SayHoldMs(line)).Seq;
        _speechIndex = 0;
        _speechDone = done;
        _speechTimer = new DispatcherTimer();
        _speechTimer.Tick += SpeechTick;
        ShowSpeechFrame();
    }

    private void SpeechTick(object? sender, EventArgs e)
    {
        if (_speechFrames is null) return;
        _speechTimer?.Stop();
        if (++_speechIndex < _speechFrames.Count) { ShowSpeechFrame(); return; }
        var done = _speechDone;
        StopSpeech();
        DrawFace(RestFace);
        RestartIdleBeats();
        done?.Invoke();
    }

    /// <summary>Test seam: fire the pending speech frame now instead of waiting on the timer.</summary>
    internal bool StepSpeech()
    {
        if (_speechTimer is null) return false;
        SpeechTick(_speechTimer, EventArgs.Empty);
        return true;
    }

    private void ShowSpeechFrame()
    {
        var frame = _speechFrames![_speechIndex];
        DrawFace(frame.Text);
        OnBubbleTextCore(frame.Bubble);
        _speechTimer!.Interval = TimeSpan.FromMilliseconds(frame.Ms);
        _speechTimer.Start();
    }

    private void StopSpeech()
    {
        if (_speechTimer is not null)
        {
            _speechTimer.Stop();
            _speechTimer.Tick -= SpeechTick;
            _speechTimer = null;
        }
        _speechFrames = null;
        _speechDone = null;
    }

    partial void OnBubbleTextCore(string? text)
    {
        if (text is null)
        {
            if (_speechBubble is not null) _speechBubble.IsVisible = false;
            if (_speechTail is not null) _speechTail.IsVisible = false;
            return;
        }
        EnsureSpeechBubble();
        _speechText!.Text = text;
        _speechBubble!.IsVisible = true;
        _speechTail!.IsVisible = true;
        LayoutBubble();
    }

    private void EnsureSpeechBubble()
    {
        if (_speechBubble is not null) return;
        var ink = new SolidColorBrush(Color.FromRgb(0xff, 0x69, 0xb4));
        var fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
        _speechText = new TextBlock
        {
            Foreground = ink, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false,
            FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace")
        };
        _speechBubble = new Border
        {
            Background = fill, BorderBrush = ink, BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 8, 10, 8), Child = _speechText, IsHitTestVisible = false
        };
        _speechTail = new Polygon { Fill = fill, Stroke = ink, StrokeThickness = 1, IsHitTestVisible = false };
        BubbleHost.Children.Add(_speechBubble);
        BubbleHost.Children.Add(_speechTail);
        _speechBubble.SizeChanged += (_, _) => LayoutBubble();
        Resized += (_, _) => PostBubbleRelayout();
        Moved += (_, _) => PostBubbleRelayout();
    }

    private void PostBubbleRelayout()
    {
        if (_closingForGood || _bubbleRelayoutPending || _speechBubble?.IsVisible != true) return;
        _bubbleRelayoutPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _bubbleRelayoutPending = false;
            if (!_closingForGood) LayoutBubble();
        }, DispatcherPriority.Background);
    }

    private void LayoutBubble()
    {
        // Runs from timer ticks and SizeChanged; a layout failure must not escape (WPF Bubble.cs:303).
        try { LayoutBubbleCore(); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] bubble layout failed"); }
    }

    private void LayoutBubbleCore()
    {
        if (_speechBubble?.IsVisible != true || _speechText is null) return;
        double bw = BodyWidth;
        double fs = Math.Max(10, Math.Round(11 * bw / 220));
        _speechText.FontSize = fs;
        _speechText.LineHeight = Math.Round(fs * 1.4);
        _speechBubble.MaxWidth = Math.Max(220, Math.Min(380, bw * 1.5));
        _speechBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Max(_speechBubble.DesiredSize.Width, _speechBubble.Bounds.Width);
        double height = Math.Max(_speechBubble.DesiredSize.Height, _speechBubble.Bounds.Height);
        double workLeft = double.NegativeInfinity, workRight = double.PositiveInfinity;
        var screen = Screens.ScreenFromWindow(this);
        if (screen is not null)
        {
            // Ask Avalonia for screen-to-local conversion; monitor DPI can differ from the window.
            workLeft = this.PointToClient(screen.WorkingArea.Position).X;
            workRight = this.PointToClient(new PixelPoint(screen.WorkingArea.Right, screen.WorkingArea.Y)).X;
        }
        var (left, flip) = EmiBubblePlacement.Resolve(
            OverlayPadX + bw * .58, OverlayPadX + bw * (1 - .58) - width,
            width, BookSide > 0, Width, workLeft, workRight);
        double top = Math.Max(2, OverlayPad + bw * BodyAspect * (1 - .96) - height);
        Canvas.SetLeft(_speechBubble, Math.Round(left));
        Canvas.SetTop(_speechBubble, Math.Round(top));
        _speechTail!.Points = flip
            ? new Points { new(0, 0), new(10, 0), new(10, 10) }
            : new Points { new(10, 0), new(0, 0), new(0, 10) };
        Canvas.SetLeft(_speechTail, Math.Round(flip ? left + width - 10 : left));
        Canvas.SetTop(_speechTail, Math.Round(top + height - 1));
    }
}
