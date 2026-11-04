// WPF Controls/Leash/LeashDrawerSection.cs:198 and Explain/LeashExplainer.cs:63.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
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
    private bool _asking, _sending;
    private string? _error;

    internal static FriendLeashOffer? Create(Friend friend, HashSet<string> sent, HashSet<string> pending, Action refresh)
    {
        var service = LeashHead.Service;
        try { return service?.Available == true ? new FriendLeashOffer(friend, service, sent, pending, refresh) : null; }
        catch { return null; }
    }

    internal FriendLeashOffer(Friend friend, ILeashService service, HashSet<string> sent, HashSet<string>? pending = null, Action? refresh = null)
    {
        _friend = friend; _service = service; _sent = sent; _pending = pending ?? new(); _refresh = refresh;
        Margin = new Thickness(0, 0, 0, 6);
        AttachedToVisualTree += (_, _) => { _service.SnapshotChanged += Changed; _service.ReceiptsChanged += Changed; Draw(); };
        DetachedFromVisualTree += (_, _) => { _service.SnapshotChanged -= Changed; _service.ReceiptsChanged -= Changed; };
        Draw();
    }
    private void Changed(LeashSnapshot _) => Draw();
    private void Changed() => Draw();
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
            if (LeashIntroRule.ShouldExplain(CoreSettings.Current, LeashIntroSide.Holder)) { _asking = true; Draw(); }
            else await Send();
        };
        Children.Add(chip);
        if (!_asking || state != LeashUiRules.OfferChip.Offer) return;
        var intro = new StackPanel { Margin = new Thickness(4, 8, 4, 8), Tag = "leash-holder-intro" };
        foreach (var key in new[] { "leash_explain_step1_holder", "leash_explain_step2_holder", "leash_explain_step3_holder", "leash_explain_step4_holder", "leash_explain_holder_see", "leash_explain_holder_cut" })
        {
            var line = FriendsDrawer.Label(Loc.Get(key), 12, FriendsDrawer.Text);
            line.TextWrapping = TextWrapping.Wrap;
            intro.Children.Add(line);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6 };
        var later = FriendsDrawer.Pill(Loc.Get("leash_explain_not_now"), FriendsDrawer.Raised, FriendsDrawer.Text, "leash-explain-not-now");
        later.Click += (_, _) => { _asking = false; Draw(); };
        var confirm = FriendsDrawer.Pill(Loc.Get("leash_explain_offer"), FriendsDrawer.Raised, FriendsDrawer.Gold, "leash-explain-offer");
        confirm.Click += async (_, _) =>
        {
            if (_sending || _pending.Contains(_friend.Id) || State() != LeashUiRules.OfferChip.Offer) return;
            _asking = false;
            if (LeashIntroRule.Record(CoreSettings.Current, LeashIntroMoment.OfferSent)) CoreSettings.Save();
            await Send();
        };
        buttons.Children.Add(later); buttons.Children.Add(confirm); intro.Children.Add(buttons); Children.Add(intro);
    }
    private async System.Threading.Tasks.Task Send()
    {
        if (_sending || State() != LeashUiRules.OfferChip.Offer || !_pending.Add(_friend.Id)) return;
        _sending = true; Draw();
        LeashSendStatus status;
        try { status = (await _service.OfferAsync(_friend.Id)).Status; }
        catch { status = LeashSendStatus.Failed; }
        if (LeashUiRules.IsGood(status)) _sent.Add(_friend.Id);
        else _error = Loc.Get(LeashUiRules.ResultKey(status));
        _pending.Remove(_friend.Id); _sending = false; Draw();
        if (LeashUiRules.IsGood(status)) _refresh?.Invoke();
    }
}
