// WPF Controls/Leash/LeashSurfaces.cs:203-219 (ask and seen), :230-255 (replace/close), :287-310 (window and Escape).
// ponytail: the accepted snap (LeashSnapCard), LeashFx thud/ask sound and the app-wide pop-up of a newly
// arrived offer (LeashSurfaces.OnSnapshot/TryShowWaitingAsk) are wire-views-tab-leash-b-next; here the
// offer row's Look opens it.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>A borderless, see-through window holding one ask card over its owner. Escape is the
/// card's own "later": the offer stays.</summary>
public sealed class LeashAskWindow : Window
{
    /// <summary>The ask on screen, if any. One at a time: a second Look replaces it (WPF Open closes the overlay first).</summary>
    internal static LeashAskWindow? Current { get; private set; }
    internal LeashAskCard Card { get; }
    // RenderProof: a sample ask.
    internal LeashAskWindow() : this(new LeashAskCard()) { }
    private LeashAskWindow(LeashAskCard card)
    {
        Card = card;
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
            card.Dismiss();
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
        w.Closed += (_, _) => { if (ReferenceEquals(Current, w)) Current = null; };
        try
        {
            if (owner is { IsVisible: true }) w.Show(owner);
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Show(); }
            w.Activate();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] ask window failed: {E}", ex.Message);
            return null;
        }
        Current = w;
        try { service()?.NoteShown(offer.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] ask seen note failed: {E}", ex.Message); }
        return w;
    }
    internal static void CloseCurrent()
    {
        var w = Current;
        Current = null;
        try { w?.Close(); } catch { }
    }
}
