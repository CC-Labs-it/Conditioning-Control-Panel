using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashSurfaces.OnSnapshot/TryShowWaitingAsk: one ask at a time, held until a window is up.
internal sealed class LeashOfferSurfaces : IDisposable
{
    private readonly Func<Window?> _owner;
    private readonly HashSet<string> _seen = new();
    private readonly List<LeashOffer> _waiting = new();
    private ILeashService? _service;
    private string? _account;
    private Window? _window;
    internal Window? AskWindow => _window;
    internal LeashOfferSurfaces(Func<Window?> owner) => _owner = owner;

    internal void Rebind()
    {
        var next = LeashHead.Service;
        var account = CoreSettings.Current.UnifiedId;
        var serviceChanged = !ReferenceEquals(next, _service);
        if (serviceChanged || account != _account)
        {
            if (_service != null) _service.SnapshotChanged -= OnSnapshot;
            _service = next; _account = account; _seen.Clear(); _waiting.Clear();
            var old = _window; _window = null; old?.Close();
            if (_service != null)
            {
                _service.SnapshotChanged += OnSnapshot;
                if (serviceChanged && _service.Available) Process(_service.Snapshot);
            }
        }
        TryShow();
    }

    private static string OfferKey(LeashOffer o) => o.From.Id + "@" + o.At.ToUnixTimeSeconds();
    private void OnSnapshot(LeashSnapshot snapshot)
    {
        var source = _service; var account = CoreSettings.Current.UnifiedId;
        void Apply()
        {
            if (!ReferenceEquals(source, _service)) return;
            if (account != CoreSettings.Current.UnifiedId) { Rebind(); return; }
            if (!ReferenceEquals(source, LeashHead.Service) || _account != CoreSettings.Current.UnifiedId) Rebind();
            if (ReferenceEquals(source, _service) && source?.Available == true) Process(snapshot);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
    }
    private void Process(LeashSnapshot snapshot)
    {
        foreach (var offer in snapshot.Offers)
        {
            if (!_seen.Add(OfferKey(offer))) continue;
            _waiting.RemoveAll(o => o.From.Id == offer.From.Id);
            _waiting.Add(offer);
        }
        _waiting.RemoveAll(o => !snapshot.Offers.Any(current => OfferKey(current) == OfferKey(o)));
        TryShow();
    }
    private void TryShow()
    {
        var owner = _owner();
        if (_service?.Available != true || _window != null || _waiting.Count == 0 || owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
        var offer = _waiting[0]; _waiting.RemoveAt(0);
        if (!Open(offer, owner)) _waiting.Insert(0, offer);
    }
    // WPF LeashDrawerSection row Look -> LeashSurfaces.ShowAsk (:209); Open (:232) closes any ask or snap first.
    internal void ShowAsk(LeashOffer offer)
    {
        var old = _window; _window = null;
        try { old?.Close(); } catch { }
        _waiting.RemoveAll(o => o.From.Id == offer.From.Id);
        Open(offer, _owner());
    }
    private bool Open(LeashOffer offer, Window? owner)
    {
        // WPF ShowAsk reads the live service (LeashSurfaces.cs:211,217), so a page Look between rebinds answers on it.
        var card = new LeashOfferCard(offer, () => LeashHead.Service);
        var screen = owner?.Screens.ScreenFromWindow(owner);
        var height = screen == null ? 700 : Math.Max(100, Math.Min(700, screen.WorkingArea.Height / screen.Scaling - 24));
        var window = new Window { Tag = "leash-auto-ask", Title = Loc.Get("leash_title"), Width = 440, MaxHeight = height, ShowInTaskbar = false, CanResize = false,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        _window = window;
        card.AskClosed += () => window.Close();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { card.Later(); e.Handled = true; } };
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_window, window)) return;
            _window = card.SnapWindow;
            if (_window is { } snap) snap.Closed += (_, _) => { if (ReferenceEquals(_window, snap)) { _window = null; TryShow(); } };
            else TryShow();
        };
        try
        {
            if (owner != null) window.Show(owner); else window.Show();
            LeashSound.Ask(DateTime.UtcNow);
        }
        catch (Exception ex) { _window = null; try { window.Close(); } catch { } Serilog.Log.Debug("[Leash] ask failed: {E}", ex.Message); return false; }
        try { if (window.IsVisible) LeashHead.Service?.NoteShown(offer.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] ask seen failed: {E}", ex.Message); }
        return true;
    }
    public void Dispose()
    {
        if (_service != null) _service.SnapshotChanged -= OnSnapshot;
        _service = null; _waiting.Clear(); _seen.Clear();
        var old = _window; _window = null; old?.Close();
    }
}
