// WPF Controls/Leash/LeashDrawerSection.cs:198 and Explain/LeashExplainer.cs:63.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

internal sealed class FriendLeashOffer : StackPanel
{
    private readonly Friend _friend;
    private readonly ILeashService _service;
    private readonly HashSet<string> _sent;
    private readonly HashSet<string> _pending;
    private readonly Action? _refresh;
    private bool _sending;
    internal LeashHolderIntro? Intro { get; private set; }
    private bool _shared;
    private string? _error;

    internal static FriendLeashOffer? Create(Friend friend, Action refresh)
    {
        LeashHead.CheckOfferAccount();
        var service = LeashHead.Service;
        try { return service?.Available == true ? new FriendLeashOffer(friend, service, LeashHead.SentOffers, LeashHead.PendingOffers, refresh) { _shared = true } : null; }
        catch { return null; }
    }
    // Retained for callers that inject their own state.
    internal static FriendLeashOffer? Create(Friend friend, HashSet<string> sent, HashSet<string> pending, Action refresh)
    {
        var service = LeashHead.Service;
        try { return service?.Available == true ? new FriendLeashOffer(friend, service, sent, pending, refresh) : null; }
        catch { return null; }
    }

    /// <summary>--render-all (RenderProof): the offer chip for a sample online friend, on a leash service whose
    /// wire answers nothing, so drawing it never reaches the network or a real account.</summary>
    internal FriendLeashOffer() : this(new Friend("render", "Mia", null, 0, true, FriendPresence.None, false),
        new LeashService(new RenderWire(), () => "render", cutSafety: () => { }), new HashSet<string>()) { }

    private sealed class RenderWire : ILeashApi
    {
        public System.Threading.Tasks.Task<Newtonsoft.Json.Linq.JObject?> CallAsync(string op, Newtonsoft.Json.Linq.JObject body, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<Newtonsoft.Json.Linq.JObject?>(null);
    }

    internal FriendLeashOffer(Friend friend, ILeashService service, HashSet<string> sent, HashSet<string>? pending = null, Action? refresh = null)
    {
        _friend = friend; _service = service; _sent = sent; _pending = pending ?? new(); _refresh = refresh;
        Margin = new Thickness(0, 0, 0, 6);
        AttachedToVisualTree += (_, _) => { _service.SnapshotChanged += Changed; _service.ReceiptsChanged += Changed; if (_shared) LeashHead.OffersChanged += Changed; Draw(); };
        DetachedFromVisualTree += (_, _) => { _service.SnapshotChanged -= Changed; _service.ReceiptsChanged -= Changed; if (_shared) LeashHead.OffersChanged -= Changed; };
        Draw();
    }
    private void Changed(LeashSnapshot _) => Draw();
    private void Changed()
    {
        if (_shared && !Current()) { _refresh?.Invoke(); return; }
        Draw();
    }
    private LeashUiRules.OfferChip State()
    {
        try
        {
            var snap = _service.Snapshot;
            return LeashUiRules.OfferState(_service.Available, snap.Holding.Any(h => h.Who.Id == _friend.Id),
                _sent.Contains(_friend.Id), _friend.Online, _friend.Presence.LastSeen, DateTimeOffset.UtcNow, snap.Holding.Count);
        }
        catch { return LeashUiRules.OfferChip.Hidden; }
    }
    private void Draw()
    {
        Children.Clear();
        var state = State();
        if (state == LeashUiRules.OfferChip.Hidden) return;
        var word = Loc.Get(state switch
        {
            LeashUiRules.OfferChip.Sent => "leash_offer_sent",
            LeashUiRules.OfferChip.Holding => "leash_offer_holding",
            _ => "leash_offer",
        });
        if (state == LeashUiRules.OfferChip.Sent)
        {
            try { if (LeashUiRules.OfferStep(_service.ReceiptsSupported, _service.SentTo(_friend.Id)) is { } step) word += " · " + Loc.Get(LeashUiRules.StepKey(step)); }
            catch { }
        }
        var chip = FriendsDrawer.Pill(_error ?? word, FriendsDrawer.Raised, FriendsDrawer.Gold, "leash-offer");
        chip.IsEnabled = !_sending && !_pending.Contains(_friend.Id) && _error == null && state == LeashUiRules.OfferChip.Offer;
        if (state == LeashUiRules.OfferChip.Greyed) ToolTip.SetTip(chip, Loc.Get("leash_offer_greyed"));
        chip.Click += async (_, _) =>
        {
            if (LeashIntroRule.ShouldExplain(CoreSettings.Current, LeashIntroSide.Holder)) ShowIntro(true);
            else await Send();
        };
        var help = FriendsDrawer.Pill("?", FriendsDrawer.Raised, FriendsDrawer.Lilac, "leash-offer-help");
        help.Width = 26; help.Padding = new Thickness(0); help.Margin = new Thickness(6, 0, 0, 0);
        help.Click += (_, _) => ShowIntro(false);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(chip); Grid.SetColumn(help, 1); row.Children.Add(help); Children.Add(row);
    }
    private void ShowIntro(bool offer)
    {
        if (Intro != null) { Intro.Activate(); return; }
        Intro = new LeashHolderIntro(_friend.Name, offer ? async () =>
        {
            if (!Current() || _sending || _pending.Contains(_friend.Id) || State() != LeashUiRules.OfferChip.Offer) return;
            if (LeashIntroRule.Record(CoreSettings.Current, LeashIntroMoment.OfferSent)) CoreSettings.Save();
            await Send();
        } : null);
        var intro = Intro;
        intro.Closed += (_, _) => { if (ReferenceEquals(Intro, intro)) Intro = null; };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner?.IsVisible == true) intro.Show(owner); else intro.Show();
    }

    private bool Current()
    {
        if (!_shared) return true;
        LeashHead.CheckOfferAccount();
        return ReferenceEquals(_service, LeashHead.Service) && ReferenceEquals(_sent, LeashHead.SentOffers);
    }
    private async System.Threading.Tasks.Task Send()
    {
        if (!Current() || _sending || State() != LeashUiRules.OfferChip.Offer || !_pending.Add(_friend.Id)) return;
        if (_shared) LeashHead.OfferChanged();
        _sending = true; Draw();
        LeashSendStatus status;
        try { status = (await _service.OfferAsync(_friend.Id)).Status; }
        catch { status = LeashSendStatus.Failed; }
        if (LeashUiRules.IsGood(status)) _sent.Add(_friend.Id);
        else _error = Loc.Get(LeashUiRules.ResultKey(status));
        _pending.Remove(_friend.Id); _sending = false; Draw();
        if (_shared && Current()) LeashHead.OfferChanged();
        if (LeashUiRules.IsGood(status)) _refresh?.Invoke();
    }
}
