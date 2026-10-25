using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private StackPanel? _speech, _answerChips;
    private TextBlock? _speechText;
    // WPF keeps the typing/ask chain and the hold (held face, parked-line release) on separate
    // timers (Bubble.cs:101 _holdTimer), so a release never cuts the reply's own typing.
    private OneShot? _speechTimerSlot, _holdTimerSlot;
    private OneShot SpeechTimer => _speechTimerSlot ??= new OneShot(this);
    private OneShot HoldTimer => _holdTimerSlot ??= new OneShot(this);
    /// <summary>Test seam (P08): start a one-shot of N ms, return its stop. Tests swap in a stepped clock.</summary>
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
    private AskDraw? _ask;
    private LineDraw? _parkedLine;
    public bool AskLive => _ask != null;
    internal string? MomentBubbleText => _speechText?.Text;
    internal Action<string> RunAskEffect = effect =>
    {
        if (effect == "book:open") EmiDeskService.Instance.Window?.OpenBook();
    };

    private void EnsureSpeech()
    {
        if (_speech != null) return;
        _speechText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Foreground = Brush.Parse("#FF69B4") };
        _answerChips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        _speech = new StackPanel { Spacing = 8, Width = 280, IsVisible = false };
        _speech.Children.Add(new Border { Background = Brush.Parse("#181832"),
            BorderBrush = Brush.Parse("#FF69B4"), BorderThickness = new Thickness(1),
            Padding = new Thickness(8), Child = _speechText });
        _speech.Children.Add(_answerChips);
        BubbleHost.Children.Add(_speech);
        // The transparent canvas has no background: only the chips take input.
        BubbleHost.IsHitTestVisible = true;
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

    private void LayoutSpeech()
    {
        if (_speech == null) return;
        double width = Math.Clamp(_bodyWidth * 1.5, 220, 380);
        _speech.Width = width;
        // Clamp both question and chips to the same visible screen interval, in window DIPs.
        double lo = 2, hi = Math.Max(lo, Width - width - 2);
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            lo = Math.Max(lo, (screen.WorkingArea.X - Position.X) / RenderScaling + 2);
            hi = Math.Min(hi, (screen.WorkingArea.Right - Position.X) / RenderScaling - width - 2);
        }
        Canvas.SetLeft(_speech, Math.Max(lo, Math.Min(hi, OverlayPadX + _bodyWidth * .58)));
        Canvas.SetTop(_speech, 8);
    }

    private void PaintSpeech(string? text)
    {
        EnsureSpeech();
        _speechText!.Text = text;
        _speech!.IsVisible = !string.IsNullOrEmpty(text);
        LayoutSpeech();
    }

    partial void OnBubbleTextCore(string? text)
    {
        if (!AskLive) PaintSpeech(text);
    }

    private void AfterSpeech(int milliseconds, Action action) => SpeechTimer.After(milliseconds, action);
    internal void StopSpeechTimer() => _speechTimerSlot?.Stop();

    private void TypeSpeech(string text, string face, Action landed)
    {
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
            PaintSpeech(null); DrawFace(EmiChains.RestFace); done?.Invoke();
        }));
    }

    public void SpeakLine(LineDraw line)
    {
        if (PresentationActive) return;
        // WPF :511: one slot, newest wins.
        if (AskLive && !line.Hold) { _parkedLine = line; return; }
        EmiLineEngine.Instance.Ack(line.Id, spoke: !line.Hold);
        if (line.Hold)
        {
            // WPF HoldFace (:545) runs on the hold timer and leaves an open question alone.
            if (!AskLive) { StopSpeechTimer(); PaintSpeech(null); }
            DrawFace(line.Face);
            HoldTimer.After(line.HoldMs > 0 ? line.HoldMs : 1400, () => DrawFace(EmiChains.RestFace));
        }
        else if (!string.IsNullOrWhiteSpace(line.Text)) Say(line.Text, line.Face);
        else DrawFace(line.Face);
        // ponytail: wordless chains and blipese need the separate chain/voice host.
    }

    public void ShowAsk(AskDraw ask)
    {
        if (PresentationActive || AskLive) return;
        EnsureSpeech();
        _ask = ask;
        _answerChips!.Children.Clear();
        EmiLineEngine.Instance.Ack(ask.Id);
        TypeSpeech(ask.Question, string.IsNullOrEmpty(ask.Face) ? "^_^" : ask.Face, () =>
        {
            for (int i = 0; i < 2; i++)
            {
                int index = i;
                var chip = new Button { Content = ask.Chips.Count >= 2 ? ask.Chips[i] : i == 0 ? "yes" : "no" };
                chip.Click += (_, e) => { e.Handled = true; AnswerAsk(index); };
                _answerChips.Children.Add(chip);
            }
            _answerChips.Children[0].Focus();
            // No expiry timer: WPF's question waits until a chip, Esc, teardown or screen takeover.
        });
    }

    internal void AnswerAsk(int index)
    {
        var ask = _ask;
        if (ask == null) return;
        ClearAsk();
        EmiLineEngine.Instance.NoteAskAnswered();
        EmiDeskService.Instance.Fire("askAnswered", new { answer = index == 0 ? "yes" : "no" });
        SpeakLine(index == 0 ? ask.Yes : ask.No);
        var effect = index == 0 ? ask.Effect : ask.EffectNo;
        try { if (!string.IsNullOrEmpty(effect)) RunAskEffect(effect); }
        finally { ReleaseParkedLine(); }
    }

    private void ClearAsk()
    {
        _ask = null; StopSpeechTimer();
        _answerChips?.Children.Clear(); PaintSpeech(null);
    }

    public void CancelAsk()
    {
        if (!AskLive) return;
        ClearAsk();
        EmiLineEngine.Instance.NoteAskIgnored();
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
        _answerChips?.Children.Clear();
        if (_speech != null) _speech.IsVisible = false;
    }

    private void OnSpeechMoment(object? sender, EmiMoment moment)
    {
        if (moment.Id is not ("videoRunning" or "lockdownArmed" or "lockdownCountdown" or "intakeOpened" or "intakeRunning" or "panicPressed")) return;
        void Cancel() { if (moment.Id == "panicPressed") StopMomentBubble(); else CancelAsk(); }
        if (Dispatcher.UIThread.CheckAccess()) Cancel(); else Dispatcher.UIThread.Post(Cancel);
    }
}
