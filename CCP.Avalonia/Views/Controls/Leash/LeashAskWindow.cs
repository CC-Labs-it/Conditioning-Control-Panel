// WPF Controls/Leash/LeashSurfaces.cs:203-219 (ask, Ask cue and seen), :221-227 (snap), :230-255 (replace/close),
// :287-310 (window and Escape). LeashOfferSurfaces pops newly arrived offers up through Open.
// Missing: the overlay Thud on load (:308-311).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
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
        Overlay(this, card, card.Dismiss);
    }
    /// <summary>The snap on screen, if any. It shares the one overlay slot with the ask (WPF LeashSurfaces._overlay).</summary>
    internal static Window? Snap { get; private set; }
    /// <summary>Raised when neither an ask nor a snap is up any more (WPF CloseOverlay -> TryShowWaitingAsk).</summary>
    internal static event Action? Freed;
    private static bool _opening;
    private static void Overlay(Window w, Control card, Action onEscape)
    {
        w.WindowDecorations = WindowDecorations.None;
        w.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        w.Background = Brushes.Transparent;
        w.ShowInTaskbar = false;
        w.CanResize = false;
        w.SizeToContent = SizeToContent.WidthAndHeight;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.Title = Loc.Get("leash_title");
        w.Content = new Grid { Margin = new Thickness(40), Children = { card } };
        w.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            onEscape();
        };
        w.Closed += (_, _) =>
        {
            if (ReferenceEquals(Current, w)) Current = null;
            if (ReferenceEquals(Snap, w)) Snap = null;
            if (!_opening && Current == null && Snap == null) Freed?.Invoke();
        };
    }
    private static bool Show(Window w, Window? owner)
    {
        try
        {
            if (owner is { IsVisible: true }) w.Show(owner);
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Show(); }
            w.Activate();
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] overlay failed: {E}", ex.Message);
            return false;
        }
    }
    /// <summary>WPF LeashSurfaces.ShowAsk: open the card, close it on an answer or a dismiss, and report
    /// the offer seen once it is up.</summary>
    internal static LeashAskWindow? Open(LeashOffer offer, Func<ILeashService?> service, Window? owner)
    {
        var w = new LeashAskWindow(new LeashAskCard(offer, service));
        w.Card.Answered += (o, accepted, _) =>
        {
            if (accepted) ShowSnap(o.From, owner); // closes this ask in the same step
            else w.Close();
        };
        w.Card.Dismissed += w.Close;
        _opening = true;
        try
        {
            CloseCurrent();
            if (!Show(w, owner)) return null;
            Current = w;
        }
        finally { _opening = false; }
        LeashSound.Ask(DateTime.UtcNow);
        try { service()?.NoteShown(offer.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] ask seen note failed: {E}", ex.Message); }
        return w;
    }
    /// <summary>WPF ShowSnap from ShowAsk's accept: holder on the left, this account (display name, else "you") on the right.</summary>
    internal static void ShowSnap(LeashPerson holder, Window? owner)
    {
        var me = string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? Loc.Get("leash_you") : CoreAccount.DisplayName!.Trim();
        ShowSnap(holder, new LeashPerson("me", me, null), owner);
    }
    internal static void ShowHolderSnap(LeashPerson leashed, Window? owner)
    {
        var me = string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? Loc.Get("leash_you") : CoreAccount.DisplayName!.Trim();
        ShowSnap(new LeashPerson("me", me, null), leashed, owner);
    }
    private static void ShowSnap(LeashPerson holder, LeashPerson leashed, Window? owner)
    {
        var card = new LeashSnapCard(holder, leashed);
        var w = new Window();
        Overlay(w, card, () => w.Close());
        card.Closed += w.Close;
        _opening = true;
        try
        {
            CloseCurrent();
            if (!Show(w, owner)) return;
            Snap = w;
        }
        finally { _opening = false; }
        card.Play();
    }
    internal static void CloseCurrent()
    {
        var (w, snap) = (Current, Snap);
        (Current, Snap) = (null, null);
        try { w?.Close(); } catch { }
        try { snap?.Close(); } catch { }
    }
}
