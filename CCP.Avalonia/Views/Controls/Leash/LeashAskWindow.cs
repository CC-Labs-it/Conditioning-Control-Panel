// WPF Controls/Leash/LeashSurfaces.cs:203-219 (ask, Ask cue and seen), :221-227 (snap), :230-255 (replace/close),
// :287-310 (window and Escape). The ask and the snap share this one overlay slot (WPF _overlay).
// Missing: the overlay Thud on load (:308-311).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>A borderless, see-through window holding one ask (or snap) card over its owner. Escape is the
/// card's own "later": the offer stays.</summary>
public sealed class LeashAskWindow : Window
{
    /// <summary>The ask or snap on screen, if any. One at a time: a second one replaces it (WPF Open closes the overlay first).</summary>
    internal static LeashAskWindow? Current { get; private set; }
    internal static event Action? CurrentClosed;
    internal static event Action<LeashOffer>? OfferShown;
    internal Control Body { get; }
    internal LeashAskCard Card => (LeashAskCard)Body;
    internal LeashSnapCard? SnapCard => Body as LeashSnapCard;
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
    /// the offer seen once it is up. An accept swaps the ask for the snap (holder left, this account right).</summary>
    internal static LeashAskWindow? Open(LeashOffer offer, Func<ILeashService?> service, Window? owner)
    {
        var w = new LeashAskWindow(new LeashAskCard(offer, service));
        w.Card.Answered += (o, accepted, _) =>
        {
            if (accepted) OpenSnap(o.From, Me(), owner);   // closes this ask in the same step
            else w.Close();
        };
        w.Card.Dismissed += w.Close;
        if (!Show(w, owner)) return null;
        OfferShown?.Invoke(offer);
        LeashSound.Ask(DateTime.UtcNow);
        try { service()?.NoteShown(offer.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] ask seen note failed: {E}", ex.Message); }
        return w;
    }
    /// <summary>WPF LeashSurfaces.ShowSnap: the holder and leashed recap in the same overlay; Got it and Escape
    /// close it. The holder side opens it from an accepted answer (MainShellWindow.LeashAsks).</summary>
    internal static LeashAskWindow? OpenSnap(LeashPerson holder, LeashPerson leashed, Window? owner)
    {
        var card = new LeashSnapCard(holder, leashed);
        var w = new LeashAskWindow(card, null);
        card.Closed += w.Close;
        if (!Show(w, owner)) return null;
        // WPF plays it once the card is laid out (the sparks need its bounds).
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(Current, w) && w.IsVisible) card.Play(); });
        return w;
    }
    /// <summary>WPF LeashSurfaces.Me: this account's display name, else "you".</summary>
    internal static LeashPerson Me()
    {
        string name;
        try { name = string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? Loc.Get("leash_you") : CoreAccount.DisplayName!.Trim(); }
        catch { name = Loc.Get("leash_you"); }
        return new LeashPerson("me", name, null);
    }
    private static bool Show(LeashAskWindow w, Window? owner)
    {
        CloseCurrent();
        w.Closed += (_, _) =>
        {
            if (!ReferenceEquals(Current, w)) return;
            Current = null;
            CurrentClosed?.Invoke();
        };
        try
        {
            if (owner is { IsVisible: true }) w.Show(owner);
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Show(); }
            w.Activate();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] overlay window failed: {E}", ex.Message);
            return false;
        }
        Current = w;
        return true;
    }
    internal static void CloseCurrent()
    {
        var w = Current;
        try { w?.Close(); } catch { Current = null; }
    }
}
