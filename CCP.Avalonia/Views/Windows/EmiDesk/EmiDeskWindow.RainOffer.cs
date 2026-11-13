using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    private AskDraw? _rainAsk;
    private EmiLineEngine? _rainAskEngine;
    internal bool RainAskLive => _rainAsk != null;

    // WPF EmiDeskService.Speak: one draw, then consume its pending ask.
    internal void FireRainOffer(EmiLineEngine? engine = null, string moment = "glassOffer")
    {
        engine ??= EmiLineEngine.Instance;
        var oldSituation = EmiLineEngine.AskSituationProbe;
        var oldFeasible = EmiLineEngine.EffectFeasibleProbe; var hasImages = EmiGifRain.Images().Count > 0;
        try
        {
            EmiLineEngine.AskSituationProbe = () => IsVisible && !PresentationActive && !InputLocked && !Transiting &&
                !RainAskLive && !CoreSession.IsSessionRunning && CoreEngine.Video?.IsPlaying != true &&
                !ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced() &&
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.WindowState != WindowState.Minimized;
            EmiLineEngine.EffectFeasibleProbe = effect => string.Equals(effect?.Trim(), "rain", StringComparison.OrdinalIgnoreCase) && hasImages;
            var ctx = EmiLineEngine.ToCtx(new { channel = "rain" });
            engine.Draw(moment, ctx);
            var ask = engine.DrawAsk(moment, ctx);
            if (ask != null && (string.IsNullOrEmpty(ask.EffectNo) || ask.EffectNo == "rain" || ask.EffectNo == "none")) ShowRainOffer(ask, engine);
        }
        finally { EmiLineEngine.AskSituationProbe = oldSituation; EmiLineEngine.EffectFeasibleProbe = oldFeasible; }
    }

    private void ShowRainOffer(AskDraw ask, EmiLineEngine engine)
    {
        _rainAsk = ask; _rainAskEngine = engine;
        engine.Ack(ask.Id);
        DrawFace(ask.Face);
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = ask.Question, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#FF69B4") });
        var buttons = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var button = new Button { Content = ask.Chips[i], Name = i == 0 ? "RainOfferYes" : "RainOfferNo" };
            button.Click += (_, _) => AnswerRainOffer(index);
            buttons.Children.Add(button);
        }
        panel.Children.Add(buttons);
        var bubble = new Border { Background = Brush.Parse("#181832"), Padding = new Thickness(8), Width = 220, Child = panel };
        Canvas.SetLeft(bubble, OverlayPadX + _bodyRoot.Width * 0.58);
        Canvas.SetTop(bubble, Math.Max(0, OverlayPad - 80));
        _bubbleCanvas.Children.Add(bubble);
        _bubbleCanvas.IsHitTestVisible = true;
        KeyDown += OnRainOfferKey;
    }

    private void OnRainOfferKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && RainAskLive) { CancelRainOffer(); e.Handled = true; }
    }

    private void AnswerRainOffer(int index)
    {
        var ask = _rainAsk; var engine = _rainAskEngine;
        if (ask == null || engine == null) return;
        if (ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced() || CoreSession.IsSessionRunning || CoreEngine.Video?.IsPlaying == true) { CancelRainOffer(); return; }
        string? effect = index == 0 ? ask.Effect : ask.EffectNo;
        var reply = index == 0 ? ask.Yes : ask.No;
        ClearRainOffer(); engine.NoteAskAnswered(); engine.Ack(reply.Id);
        DrawFace(reply.Face);
        if (string.Equals(effect, "rain", StringComparison.OrdinalIgnoreCase)) EmiGifRain.Start(this);
    }

    private void CancelRainOffer()
    {
        if (_rainAsk == null) return;
        var engine = _rainAskEngine;
        ClearRainOffer(); engine?.NoteAskIgnored();
    }

    private void ClearRainOffer()
    {
        _rainAsk = null; _rainAskEngine = null;
        KeyDown -= OnRainOfferKey;
        _bubbleCanvas.Children.Clear(); _bubbleCanvas.IsHitTestVisible = false;
    }
}
