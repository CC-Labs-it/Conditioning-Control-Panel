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
    private IReadOnlyList<EmiSayFrame> _speechFrames = Array.Empty<EmiSayFrame>();
    private int _speechIndex;
    private Action? _speechDone;
    private bool _speechHooked;
    private DispatcherTimer? _holdTimer;
    internal bool SpeechActive => _speechTimer?.IsEnabled == true;

    private void StartSpeech(string? line, string face, Action? done)
    {
        CancelSpeech();
        if (PresentationActive) return;
        _speechFrames = EmiSayFrames.Create(line, face, EmiSayFrames.HoldMs(line));
        _speechIndex = 0;
        _speechDone = done;
        StopIdleBeats();
        _speechTimer ??= new DispatcherTimer(DispatcherPriority.Normal);
        _speechTimer.Tick -= AdvanceSpeech;
        _speechTimer.Tick += AdvanceSpeech;
        HookSpeechLifetime();
        AdvanceSpeech(null, EventArgs.Empty);
    }

    private void HookSpeechLifetime()
    {
        if (!_speechHooked)
        {
            _speechHooked = true;
            Closed += (_, _) => CancelSpeech();
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) CancelSpeech(); };
            Resized += (_, _) => PlaceSpeechBubble();
            Moved += (_, _) => PlaceSpeechBubble();
        }
    }

    private void AdvanceSpeech(object? sender, EventArgs args)
    {
        _speechTimer?.Stop();
        if (_speechIndex >= _speechFrames.Count)
        {
            var done = _speechDone;
            _speechDone = null;
            RestartIdleBeats();
            done?.Invoke();
            return;
        }
        var frame = _speechFrames[_speechIndex++];
        DrawFace(frame.Face);
        OnBubbleTextCore(frame.Bubble);
        _speechTimer!.Interval = TimeSpan.FromMilliseconds(frame.Ms);
        _speechTimer.Start();
    }

    private void CancelSpeech()
    {
        _speechTimer?.Stop();
        _holdTimer?.Stop();
        _holdTimer = null;
        _speechDone = null;
        _speechFrames = Array.Empty<EmiSayFrame>();
        HideSpeechBubble();
    }

    /// <summary>WPF SpeakLine: acknowledge only once the first dot reaches the widget.</summary>
    internal bool SpeakRingLine(LineDraw line, EmiLineEngine engine)
    {
        if (PresentationActive || !IsVisible) return false;
        if (line.Hold)
        {
            HookSpeechLifetime();
            CancelChain(); StopIdleBeats();
            DrawFace(string.IsNullOrEmpty(line.Face) ? "-_-" : line.Face);
            engine.Ack(line.Id, spoke: false);
            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(line.HoldMs > 0 ? line.HoldMs : 1400) };
            _holdTimer.Tick += (_, _) => { _holdTimer?.Stop(); _holdTimer = null; DrawFace(RestFace); RestartIdleBeats(); };
            _holdTimer.Start();
            return false;
        }
        engine.Ack(line.Id);
        if (string.IsNullOrWhiteSpace(line.Text)) DrawFace(line.Face);
        else Say(line.Text, string.IsNullOrEmpty(line.Face) ? "^_^" : line.Face);
        return true;
    }

    partial void OnBubbleTextCore(string? text)
    {
        if (string.IsNullOrEmpty(text)) { HideSpeechBubble(); return; }
        if (_speechBubble == null)
        {
            var ink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            var fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x32));
            _speechText = new TextBlock
            {
                FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"),
                Foreground = ink, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false
            };
            _speechBubble = new Border
            {
                Background = fill, BorderBrush = ink, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8), IsHitTestVisible = false, Child = _speechText
            };
            _speechTail = new Polygon { Fill = fill, Stroke = ink, StrokeThickness = 1, IsHitTestVisible = false };
            BubbleHost.Children.Add(_speechTail);
            BubbleHost.Children.Add(_speechBubble);
            _speechBubble.SizeChanged += (_, _) => PlaceSpeechBubble();
        }
        _speechText!.Text = text;
        _speechBubble.IsVisible = true;
        _speechTail!.IsVisible = true;
        PlaceSpeechBubble();
    }

    private void HideSpeechBubble()
    {
        if (_speechBubble != null) _speechBubble.IsVisible = false;
        if (_speechTail != null) _speechTail.IsVisible = false;
    }

    private void PlaceSpeechBubble()
    {
        if (_speechBubble?.IsVisible != true || _speechText == null) return;
        var bw = BodyWidth;
        var fs = Math.Max(10, Math.Round(11 * bw / 220));
        _speechText.FontSize = fs;
        _speechText.LineHeight = Math.Round(fs * 1.4);
        _speechBubble.MaxWidth = Math.Clamp(bw * 1.5, 220, 380);
        _speechBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = Math.Max(_speechBubble.DesiredSize.Width, _speechBubble.Bounds.Width);
        var h = Math.Max(_speechBubble.DesiredSize.Height, _speechBubble.Bounds.Height);
        double workLeft = double.NegativeInfinity, workRight = double.PositiveInfinity;
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            var area = screen.WorkingArea;
            workLeft = this.PointToClient(new PixelPoint(area.X, area.Y)).X;
            workRight = this.PointToClient(new PixelPoint(area.Right, area.Y)).X;
        }
        // ponytail: the book-side arbitration awaits the book's SideOfHer event bridge.
        var (left, flip) = EmiBubblePlacement.Resolve(OverlayPadX + bw * .58,
            OverlayPadX + bw * .42 - w, w, false, Width, workLeft, workRight);
        var top = Math.Max(2, OverlayPad + bw * BodyAspect * .04 - h);
        Canvas.SetLeft(_speechBubble, Math.Round(left));
        Canvas.SetTop(_speechBubble, Math.Round(top));
        _speechTail!.Points = new List<Point>(flip
            ? new[] { new Point(left + w - 22, top + h - 1), new Point(left + w - 12, top + h + 9), new Point(left + w - 12, top + h - 1) }
            : new[] { new Point(left + 12, top + h - 1), new Point(left + 12, top + h + 9), new Point(left + 22, top + h - 1) });
    }
}
