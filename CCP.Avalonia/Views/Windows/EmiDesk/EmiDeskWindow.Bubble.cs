using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Localization;
namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
public partial class EmiDeskWindow
{
    private StackPanel? _speech, _answerChips;
    private TextBlock? _speechText;
    private OneShot? _speechTimerSlot, _holdTimerSlot;
    private OneShot SpeechTimer => _speechTimerSlot ??= new OneShot(this);
    private OneShot HoldTimer => _holdTimerSlot ??= new OneShot(this);
    internal Func<int, Action, Action> StartTimer = (ms, tick) =>
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); tick(); };
        timer.Start();
        return timer.Stop;
    };
    private sealed class OneShot(EmiDeskWindow owner)
    {
        private Action? _stop;
        private int _epoch;
        public void After(int ms, Action action)
        {
            Stop();
            int epoch = _epoch;
            _stop = owner.StartTimer(ms, () => { if (epoch == _epoch) { _stop = null; action(); } });
        }
        public void Stop() { _stop?.Invoke(); _stop = null; _epoch++; }
    }
    internal Func<PixelPoint, Point>? BubbleClientPoint;
    private bool _speechLive;
    private AskDraw? _ask;
    private LineDraw? _parkedLine;
    public bool AskLive => _ask != null;
    internal string? MomentBubbleText => _speechText?.Text;
    internal TextBlock? MomentBubbleTextBlock => _speechText;
    internal Polygon? MomentBubbleTail { get; private set; }
    private static readonly FontFamily BubbleFont =
        new("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace");
    internal Action<string> RunAskEffect = effect =>
    {
        if (effect == "book:open") EmiDeskService.Instance.Window?.OpenBook();
    };
    private void EnsureSpeech()
    {
        if (_speech != null) return;
        _speechText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11,
            FontFamily = BubbleFont, Foreground = Brush.Parse("#FF69B4") };
        _answerChips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, IsVisible = false };
        _speech = new StackPanel { Spacing = 2, MaxWidth = 280, IsVisible = false };
        MomentBubbleTail = new Polygon { Fill = Brush.Parse("#181832"), Stroke = Brush.Parse("#FF69B4"),
            StrokeThickness = 1, Margin = new Thickness(0, -1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Points = new System.Collections.Generic.List<Point> { new(10, 0), new(0, 0), new(0, 10) } };
        _speech.Children.Add(new StackPanel { IsHitTestVisible = false, Children =
        {
            new Border { Background = Brush.Parse("#181832"),
                BorderBrush = Brush.Parse("#FF69B4"), BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 8, 10, 8), Child = _speechText },
            MomentBubbleTail,
        } });
        _speech.Children.Add(_answerChips); BubbleHost.Children.Add(_speech);
        BubbleHost.IsHitTestVisible = false;
        _speech.SizeChanged += (_, _) => LayoutSpeech();
        Resized += (_, _) => LayoutSpeech();
        Moved += (_, _) => LayoutSpeech();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && AskLive) { CancelAsk(); e.Handled = true; } };
        EmiDeskService.Instance.MomentFired += OnSpeechMoment;
        Closed += (_, _) =>
        {
            EmiDeskService.Instance.MomentFired -= OnSpeechMoment;
            StopMomentBubble();
        };
        LayoutSpeech();
    }
    internal void LayoutSpeech()
    {
        if (_speech is not { IsVisible: true }) return;
        double bw = _bodyWidth;
        _speech.MaxWidth = Math.Clamp(bw * 1.5, 220, 380);
        _speechText!.FontSize = Math.Max(10, Math.Round(11 * bw / 220)); _speechText.LineHeight = Math.Round(_speechText.FontSize * 1.4);
        _speech.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = Math.Max(_speech.DesiredSize.Width, _speech.Bounds.Width); var body = _speech.Children[0];
        double height = Math.Max(body.DesiredSize.Height, body.Bounds.Height) - 10; double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            Func<PixelPoint, Point> toClient = BubbleClientPoint ?? this.PointToClient;
            lo = toClient(screen.WorkingArea.TopLeft).X;
            hi = toClient(screen.WorkingArea.BottomRight).X;
        }
        var (left, flip) = EmiBubblePlacement.Resolve(OverlayPadX + bw * .58,
            OverlayPadX + bw * .42 - width, width, _book?.SideOfHer > 0, Width, lo, hi);
        Canvas.SetLeft(_speech, Math.Round(left));
        Canvas.SetTop(_speech, Math.Round(Math.Max(2, OverlayPad + bw * BodyAspect * .04 - height)));
        _answerChips!.HorizontalAlignment = flip ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        MomentBubbleTail!.HorizontalAlignment = _answerChips.HorizontalAlignment;
        MomentBubbleTail.Points = new System.Collections.Generic.List<Point> { new(flip ? 0 : 10, 0), new(flip ? 10 : 0, 0), new(flip ? 10 : 0, 10) };
    }
    private void PaintSpeech(string? text)
    {
        EnsureSpeech();
        _speechText!.Text = text; _speech!.IsVisible = !string.IsNullOrEmpty(text);
        LayoutSpeech();
    }
    partial void OnBubbleTextCore(string? text)
    {
        if (!AskLive) PaintSpeech(text);
    }
    private void AfterSpeech(int milliseconds, Action action) => SpeechTimer.After(milliseconds, action);
    internal void StopSpeechTimer() { _speechTimerSlot?.Stop(); _speechLive = false; }
    private void TypeSpeech(string text, string face, Action landed)
    {
        StopSpeechTimer(); StopPatFeedback(); _speechLive = true;
        DrawFace(EmiChains.RestFace);
        PaintSpeech(".");
        AfterSpeech(420, () => { PaintSpeech("..");
            AfterSpeech(420, () => { PaintSpeech("...");
                AfterSpeech(520, () => { DrawFace(face); PaintSpeech(text); landed(); }); }); });
    }
    private void StartSay(string? text, string face, Action? done)
    {
        if (AskLive) return;
        if (string.IsNullOrWhiteSpace(text)) { done?.Invoke(); return; }
        TypeSpeech(text, face, () => AfterSpeech(EmiChains.SayHoldMs(text), () =>
        {
            StopSpeechTimer(); PaintSpeech(null); DrawFace(EmiChains.RestFace); done?.Invoke();
        }));
    }
    public void SpeakLine(LineDraw line)
    {
        if (PresentationActive) return;
        if (AskLive && !line.Hold) { _parkedLine = line; return; }
        _holdTimerSlot?.Stop();
        EmiLineEngine.Instance.Ack(line.Id, spoke: !line.Hold);
        if (line.Hold)
        {
            StopPatFeedback();
            if (!AskLive) { StopSpeechTimer(); PaintSpeech(null); }
            DrawFace(line.Face);
            HoldTimer.After(line.HoldMs > 0 ? line.HoldMs : 1400, () => DrawFace(EmiChains.RestFace));
        }
        else if (!string.IsNullOrWhiteSpace(line.Text)) Say(line.Text, line.Face);
        else DrawFace(line.Face);
    }
    public void ShowAsk(AskDraw ask)
    {
        if (PresentationActive || AskLive) return;
        _holdTimerSlot?.Stop();
        EnsureSpeech();
        _ask = ask; ClearChips();
        EmiLineEngine.Instance.Ack(ask.Id);
        TypeSpeech(ask.Question, string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face, () =>
        {
            _speechLive = false;
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var chip = new Button { Content = ask.Chips.Count >= 2 ? ask.Chips[i] : Loc.Get(i == 0 ? "btn_yes" : "btn_no") };
                chip.Click += (_, e) => { e.Handled = true; AnswerAsk(index); };
                _answerChips.Children.Add(chip);
            }
            _answerChips.IsVisible = true; BubbleHost.IsHitTestVisible = true; LayoutSpeech();
            _answerChips.Children[0].Focus();
        });
    }
    internal void AnswerAsk(int index)
    {
        var ask = _ask; if (ask == null) return;
        ClearAsk(); EmiLineEngine.Instance.NoteAskAnswered();
        EmiDeskService.Instance.Fire("askAnswered", new { answer = index == 0 ? "yes" : "no" });
        SpeakLine(index == 0 ? ask.Yes : ask.No);
        var effect = index == 0 ? ask.Effect : ask.EffectNo;
        try { if (!string.IsNullOrEmpty(effect)) RunAskEffect(effect); }
        finally { ReleaseParkedLine(); }
    }
    private void ClearChips() { _answerChips?.Children.Clear(); if (_answerChips != null) _answerChips.IsVisible = false; BubbleHost.IsHitTestVisible = false; }
    private void ClearAsk()
    {
        _ask = null; StopSpeechTimer();
        ClearChips(); PaintSpeech(null);
    }
    public void CancelAsk()
    {
        if (!AskLive) return;
        ClearAsk(); StopPatFeedback(); EmiLineEngine.Instance.NoteAskIgnored();
        EmiDeskService.Instance.Fire("askIgnored");
        DrawFace("-_-");
        AfterSpeech(1400, () => { PaintSpeech("..."); AfterSpeech(700, () =>
        {
            PaintSpeech(null); DrawFace(EmiChains.RestFace); ReleaseParkedLine();
        }); });
    }
    private void ReleaseParkedLine()
    {
        var line = _parkedLine; _parkedLine = null;
        if (line != null) HoldTimer.After(900, () => SpeakLine(line)); // WPF :855
    }
    internal void StopMomentBubble()
    {
        if (AskLive) { EmiLineEngine.Instance.NoteAskIgnored(); _ask = null; }
        _parkedLine = null; StopSpeechTimer(); _holdTimerSlot?.Stop();
        ClearChips();
        if (_speech != null) _speech.IsVisible = false;
    }
    private void OnSpeechMoment(object? sender, EmiMoment moment)
    {
        if (moment.Id is not ("videoRunning" or "lockdownArmed" or "lockdownCountdown" or "intakeOpened" or "intakeRunning" or "panicPressed")) return;
        void Cancel() { if (moment.Id == "panicPressed") StopMomentBubble(); else CancelAsk(); }
        if (Dispatcher.UIThread.CheckAccess()) Cancel(); else Dispatcher.UIThread.Post(Cancel);
    }
}
