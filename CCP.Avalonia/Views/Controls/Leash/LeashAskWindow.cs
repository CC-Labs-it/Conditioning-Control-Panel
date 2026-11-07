// WPF Controls/Leash/LeashSurfaces.cs:203-219 (ask and seen), :222-255 (snap, replace/close), :287-310 (window and Escape).
// ponytail: LeashFx ask sound and the app-wide pop-up of a newly arrived offer (LeashSurfaces.OnSnapshot/
// TryShowWaitingAsk) are wire-views-tab-leash-b-next; here the offer row's Look and the holder menu's Replay open it.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>WPF LeashOverlayWindow: a borderless, see-through window holding one leash card (an ask or a
/// snap) over its owner. Escape is the card's own "later": an offer stays.</summary>
public sealed class LeashAskWindow : Window
{
    /// <summary>The card on screen, if any. One at a time: a second ask or snap replaces it (WPF Open closes the overlay first).</summary>
    internal static LeashAskWindow? Current { get; private set; }
    internal Control Body { get; }
    internal LeashAskCard Card => (LeashAskCard)Body;
    // RenderProof: a sample ask.
    internal LeashAskWindow() : this(new LeashAskCard()) { }
    private LeashAskWindow(LeashAskCard card) : this(card, card.Dismiss) { }
    private LeashAskWindow(Control card, Action? onEscape)
    {
        Body = card;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = Loc.Get("leash_title");
        Content = new Grid { Margin = new Thickness(40), Children = { card } };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (onEscape != null) onEscape(); else Close();
        };
    }
    /// <summary>WPF LeashSurfaces.ShowAsk: open the card, close it on an answer or a dismiss, and report
    /// the offer seen once it is up.</summary>
    internal static LeashAskWindow? Open(LeashOffer offer, Func<ILeashService?> service, Window? owner)
    {
        CloseCurrent();
        var w = new LeashAskWindow(new LeashAskCard(offer, service));
        w.Card.Answered += (_, _, _) => w.Close();
        w.Card.Dismissed += w.Close;
        if (!w.ShowOver(owner)) return null;
        try { service()?.NoteShown(offer.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] ask seen note failed: {E}", ex.Message); }
        return w;
    }
    /// <summary>WPF LeashSurfaces.ShowSnap: the holder and leashed recap in the same overlay; Got it and Escape close it.</summary>
    internal static LeashAskWindow? OpenSnap(LeashPerson holder, LeashPerson leashed, Window? owner)
    {
        CloseCurrent();
        var card = new LeashSnapCard(holder, leashed, AmbientFxCanvas.Env.Level);
        var w = new LeashAskWindow(card, null);
        card.Closed += w.Close;
        if (!w.ShowOver(owner)) return null;
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(Current, w) && w.IsVisible) card.Play(); });
        return w;
    }
    private bool ShowOver(Window? owner)
    {
        Closed += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
        try
        {
            if (owner is { IsVisible: true }) Show(owner);
            else { WindowStartupLocation = WindowStartupLocation.CenterScreen; Show(); }
            Activate();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] overlay window failed: {E}", ex.Message);
            return false;
        }
        Current = this;
        return true;
    }
    internal static void CloseCurrent()
    {
        var w = Current;
        Current = null;
        try { w?.Close(); } catch { }
    }
}
