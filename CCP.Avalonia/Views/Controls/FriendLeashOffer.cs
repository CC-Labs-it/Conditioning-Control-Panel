// WPF Controls/Leash/LeashDrawerSection.cs:198 and Explain/LeashExplainer.cs:63.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Automation;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
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
    private static readonly HashSet<FriendLeashOffer> Popping = new();
    internal static void StopForPanic() { foreach (var offer in Popping.ToArray()) offer.StopPop(); }
    private BeatLoop? _pop;
    private ScaleTransform? _popScale;
    internal ScaleTransform? PopScale => _popScale;
    private double _popAmount;
    private void StopPop()
    {
        _pop?.Stop(); _pop = null;
        if (_popScale != null) _popScale.ScaleX = _popScale.ScaleY = 1;
        _popScale = null; Popping.Remove(this);
    }
    internal void Pop(Button chip)
    {
        StopPop();
        _popAmount = FriendsDrawer.FeedMotionAmount;
        if (_popAmount <= 0) return;
        chip.RenderTransformOrigin = RelativePoint.Center;
        chip.RenderTransform = _popScale = new ScaleTransform(1, 1);
        _pop = new BeatLoop(chip, seconds => AdvancePop(seconds * 1000));
        _pop.OnParked = StopPop;
        Popping.Add(this);
        _pop.Start();
    }
    internal void AdvancePop(double ms)
    {
        if (_popScale == null) return;
        double p = Math.Clamp(ms / 100, 0, 1);
        double v = ms <= 100 ? 1 - (1 - p) * (1 - p) :
            Math.Exp(-4 * Math.Clamp((ms - 100) / 260, 0, 1)) * Math.Cos(2 * Math.PI * Math.Clamp((ms - 100) / 260, 0, 1));
        _popScale.ScaleX = _popScale.ScaleY = ms >= 360 ? 1 : 1 + 0.18 * _popAmount * v;
        if (ms >= 360) StopPop();
    }
    internal Action<LeashCue> PlayCue = cue => { if (cue == LeashCue.Sent) LeashSound.Sent(DateTime.UtcNow); else LeashSound.Denied(DateTime.UtcNow); };
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
        DetachedFromVisualTree += (_, _) => { StopPop(); _service.SnapshotChanged -= Changed; _service.ReceiptsChanged -= Changed; if (_shared) LeashHead.OffersChanged -= Changed; };
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
        bool popping = _pop != null;
        StopPop();
        Children.Clear();
        var state = State();
        if (state == LeashUiRules.OfferChip.Hidden) return;
        var word = Loc.Get(state switch
        {
            LeashUiRules.OfferChip.Sent => "leash_offer_sent",
            LeashUiRules.OfferChip.Holding => "leash_offer_holding",
            _ => "leash_offer",
        });
        // WPF #5650 LeashDrawerSection.cs:210-265: the offer is a pressed paper tile.
        var edge = new SolidColorBrush(state switch
        {
            LeashUiRules.OfferChip.Sent => Color.Parse("#1FA877"),
            LeashUiRules.OfferChip.Greyed => Color.Parse("#7A6A8F"),
            _ => Color.Parse("#B07A00"),
        });
        bool done = state is LeashUiRules.OfferChip.Sent or LeashUiRules.OfferChip.Holding;
        var content = FriendsDrawer.GlyphContent(IconKind.Link, _error ?? word, glyphFg: Brushes.White);
        var glyph = content.Children[0];
        content.Children.RemoveAt(0);
        glyph.Margin = new Thickness(0);
        content.Children.Insert(0, new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(5),
            Background = edge, Margin = new Thickness(0, 0, 8, 0), Child = glyph });
        var label = (TextBlock)content.Children[1];
        label.Foreground = new SolidColorBrush(Color.Parse("#2E183A"));
        label.FontWeight = FontWeight.Bold; label.FontSize = 16;
        if (state == LeashUiRules.OfferChip.Sent)
        {
            try
            {
                if (LeashUiRules.OfferStep(_service.ReceiptsSupported, _service.SentTo(_friend.Id)) is { } step)
                    content.Children.Add(new TextBlock { Text = "  ·  " + Loc.Get(LeashUiRules.StepKey(step)),
                        Tag = "leash-offer-step:" + step.ToString().ToLowerInvariant(),
                        FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#4B1F45")), VerticalAlignment = VerticalAlignment.Center });
            }
            catch { }
        }
        // The face moves onto the coloured ledge when pressed or already sent/held.
        var chip = Tile(content, edge, done, "leash-offer", word);
        chip.Opacity = state == LeashUiRules.OfferChip.Greyed ? 0.5 : 1;
        chip.IsEnabled = !_sending && !_pending.Contains(_friend.Id) && _error == null && state == LeashUiRules.OfferChip.Offer;
        if (state == LeashUiRules.OfferChip.Greyed) ToolTip.SetTip(chip, Loc.Get("leash_offer_greyed"));
        chip.Click += async (_, _) =>
        {
            Pop(chip);
            if (LeashIntroRule.ShouldExplain(CoreSettings.Current, LeashIntroSide.Holder)) ShowIntro(true);
            else await Send();
        };
        var helpText = new TextBlock { Text = "?", FontSize = 18, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#2E183A")), HorizontalAlignment = HorizontalAlignment.Center };
        var help = Tile(helpText, edge, false, "leash-offer-help", Loc.Get("leash_help"));
        help.Width = 40; help.Margin = new Thickness(3, 0, 0, 0);
        help.VerticalAlignment = VerticalAlignment.Stretch;
        ToolTip.SetTip(help, Loc.Get("leash_help"));
        help.Click += (_, _) => ShowIntro(false);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Tag = "leash-offer-chip:" + state.ToString().ToLowerInvariant() };
        row.Children.Add(chip); Grid.SetColumn(help, 1); row.Children.Add(help); Children.Add(row);
        if (popping) Pop(chip);
    }
    private static Button Tile(Control content, IBrush edge, bool done, string tag, string name)
    {
        var move = new TranslateTransform(0, done ? 3 : 0);
        var face = new Border { Tag = "leash-tile-face", Background = done ? Brushes.White : new SolidColorBrush(Color.Parse("#F7EEF8")),
            BorderBrush = new SolidColorBrush(Color.Parse(done ? "#3A1D78" : "#DCC6DD")), BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(6, 5, 7, 5), Margin = new Thickness(0, 0, 0, 4), Child = content, RenderTransform = move };
        var grid = new Grid();
        grid.Children.Add(new Border { Background = edge, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 4, 0, 0) });
        grid.Children.Add(face);
        var button = FriendsDrawer.Pill(grid, Brushes.Transparent, Brushes.Transparent, tag);
        button.Padding = new Thickness(0); button.BorderThickness = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, name);
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (button.IsEnabled && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) move.Y = 3; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => move.Y = done ? 3 : 0, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerExited += (_, _) => move.Y = done ? 3 : 0;
        return button;
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
        // WPF LeashDrawerSection.cs:281-282: Sent on a good answer, Denied on too fast, do not disturb or a failure.
        if (LeashUiRules.IsGood(status)) { _sent.Add(_friend.Id); PlayCue(LeashCue.Sent); }
        else if (status is LeashSendStatus.TooFast or LeashSendStatus.Dnd or LeashSendStatus.Failed) PlayCue(LeashCue.Denied);
        if (!LeashUiRules.IsGood(status)) _error = Loc.Get(LeashUiRules.ResultKey(status));
        _pending.Remove(_friend.Id); _sending = false; Draw();
        if (_shared && Current()) LeashHead.OfferChanged();
        if (LeashUiRules.IsGood(status)) _refresh?.Invoke();
    }
}
