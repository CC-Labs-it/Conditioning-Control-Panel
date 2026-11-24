using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

// WPF 1468acc21b Dialogs/Dares/DareSlipWindow.cs:25-442. The host supplies its live service.
public sealed class DareSlipWindow : Window
{
    private static DareSlipWindow? _open;
    private readonly IDaresService _service;
    private readonly IReadOnlyList<DareFriend> _friends;
    private readonly IReadOnlyDictionary<string, int>? _tiers;
    private readonly DareDraft _draft = new();
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly StackPanel _sentence = new();
    private readonly StackPanel _tray = new() { Spacing = 6 };
    private readonly Border _card;
    private bool _sending;
    private DareCreateOutcome? _result;

    internal DareDraft Draft => _draft;

    internal DareSlipWindow(IDaresService service, DareFriend? who, IReadOnlyList<DareFriend> friends,
        IReadOnlyDictionary<string, int>? tiers, bool introSeen, Action markSeen)
    {
        _service = service; _friends = friends; _tiers = tiers;
        if (who != null) { _draft.Who = who; _draft.WhoTier = TierOf(who.Id); }
        Width = 560; SizeToContent = SizeToContent.Height; CanResize = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33));
        _card = new Border { Tag = "dares-slip-window", Padding = new Thickness(20) };
        Content = _card;
        if (introSeen) ShowComposer();
        else _card.Child = DareIntro.Build(() => { markSeen(); ShowComposer(); });
    }

    public static void Open(Window? owner, IDaresService? service, DareFriend? who, IReadOnlyList<DareFriend> friends,
        IReadOnlyDictionary<string, int>? tiers = null)
    {
        if (service == null) return;
        try { if (!service.Snapshot.Available) return; } catch { return; }
        try { _open?.Close(); } catch (InvalidOperationException) { }
        var window = new DareSlipWindow(service, who, friends, tiers, DareIntro.Seen(), DareIntro.MarkSeen);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        if (owner is { IsVisible: true }) window.Show(owner); else window.Show();
    }

    private void ShowComposer()
    {
        _body.Children.Clear();
        var head = new DockPanel();
        var close = new Button { Content = "×", Tag = "dares-close" };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = Loc.Get("dares_slip_title"), FontSize = 22, FontWeight = FontWeight.Bold });
        _body.Children.Add(head);
        _body.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x23, 0x16, 0x40)),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16), MinHeight = 120, Child = _sentence });
        _body.Children.Add(_tray); _card.Child = _body; Render();
    }

    private Button Choice(string text, DareTone tone, string? tip, string tag, Action action, bool enabled = true)
    {
        var b = DareLook.Choice(text, DareLook.ToneColor(tone), tip == null ? null : Loc.Get(tip), action, tag);
        b.IsEnabled = enabled; return b;
    }

    private void Press(Action action) { FriendsSfx.Click(); action(); Render(); }
    private void Render()
    {
        _sentence.Children.Clear();
        foreach (var line in DareSlipRules.Sentence(_draft, Loc.Get))
        {
            var row = new WrapPanel();
            foreach (var seg in line.Segs)
            {
                if (seg.Kind == DareSegKind.Chip && seg.Step is { } step && _result == null)
                    row.Children.Add(Choice(seg.Text, seg.Tone, seg.TipKey, "dares-chip:" + step, () => Press(() => DareSlipRules.Edit(_draft, step))));
                else row.Children.Add(new TextBlock { Text = seg.Text, VerticalAlignment = VerticalAlignment.Center, FontSize = 15 });
            }
            _sentence.Children.Add(row);
        }
        RenderTray();
    }

    private void RenderTray()
    {
        _tray.Children.Clear();
        if (_result != null) { RenderResult(_result); return; }
        var current = DareSlipRules.Current(_draft);
        if (current == null)
        {
            var send = Choice(Loc.Get("dares_send"), DareTone.Duel, null, "dares-send", () => _ = SendAsync(), !_sending);
            _tray.Children.Add(send);
            if (DareSlipRules.ToSlip(_draft) is { } slip && DareSlipRules.HasDare(slip))
                _tray.Children.Add(new TextBlock { Text = Loc.Get("dares_send_dare_note"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var step = current.Value;
        _tray.Children.Add(new TextBlock { Text = Loc.Get(DareSlipRules.AskKey(step, _draft.Type)).ToUpperInvariant() });
        if (DareSlipRules.HelpFor(step, _draft) is { } help) _tray.Children.Add(new TextBlock { Text = Loc.Get(help), TextWrapping = TextWrapping.Wrap });
        var tiles = new WrapPanel { Tag = "dares-tray:" + step }; _tray.Children.Add(tiles);
        DaresMe me; try { me = _service.Snapshot.Me; } catch { me = DaresSnapshot.Dark.Me; }
        switch (step)
        {
            case DareStep.Who:
                foreach (var f in _friends) tiles.Children.Add(Choice(f.Name, DareTone.Who, null, "dares-who:" + f.Id, () => Press(() => DareSlipRules.SetWho(_draft, f, TierOf(f.Id)))));
                if (_friends.Count == 0) tiles.Children.Add(new TextBlock { Text = Loc.Get("dares_no_friends"), TextWrapping = TextWrapping.Wrap });
                break;
            case DareStep.How:
                foreach (var t in new[] { DareType.Cant, DareType.Duel })
                    tiles.Children.Add(Choice(Loc.Get(DareSlipRules.TileKey(t)), t == DareType.Duel ? DareTone.Duel : DareTone.Cant,
                        t == DareType.Duel ? "dares_tip_duel" : "dares_tip_cant", "dares-tile:" + DareGrammar.Wire(t), () => Press(() => DareSlipRules.SetType(_draft, t))));
                break;
            case DareStep.What:
                foreach (var k in DareSlipRules.Kinds(_draft.Type))
                {
                    var shut = DareSlipRules.KindShut(k, me);
                    tiles.Children.Add(Choice(Loc.Get(DareSlipRules.TileKey(k)), DareSlipRules.ToneOf(k), shut ?? DareSlipRules.TipOf(k),
                        "dares-tile:" + DareGrammar.Wire(k), () => Press(() => DareSlipRules.PickKind(_draft, k)), shut == null));
                }
                if (_draft.Pending is DareKind kind)
                    foreach (var target in DareSlipRules.TargetsFor(_draft.Type, kind))
                        tiles.Children.Add(Choice(kind == DareKind.Session ? Loc.Get(DareSlipRules.TargetLabelKey(target)) : target,
                            DareSlipRules.ToneOf(kind), DareSlipRules.TipOf(kind), "dares-target:" + target, () => Press(() => DareSlipRules.PickTarget(_draft, target))));
                break;
            case DareStep.When:
                foreach (var deadline in Enum.GetValues<DareDeadline>()) tiles.Children.Add(Choice(Loc.Get(DareSlipRules.WhenKey(deadline)), DareTone.When,
                    "dares_tip_when", "dares-when:" + DareGrammar.Wire(deadline), () => Press(() => DareSlipRules.SetDeadline(_draft, deadline))));
                break;
            case DareStep.Mine:
            case DareStep.Theirs:
                foreach (var k in DareSlipRules.StakeKinds)
                {
                    var (state, why) = DareSlipRules.StakeLock(k, step, me, TierGate.HasPremium, _draft.WhoTier);
                    tiles.Children.Add(Choice(Loc.Get(DareSlipRules.TileKey(k)), DareSlipRules.ToneOf(k), why ?? DareSlipRules.TipOf(k),
                        "dares-stake:" + DareGrammar.Wire(k), () =>
                        {
                            if (state == DareTileLock.Upsell)
                            {
                                FriendsSfx.Click();
                                if (Owner is MainShellWindow shell) shell.BtnGateUnlock_Click(null, new RoutedEventArgs());
                            }
                            else Press(() => DareSlipRules.PickStakeKind(_draft, k));
                        }, state != DareTileLock.Shut));
                }
                if (_draft.Pending is DareStakeKind stake)
                {
                    int? balance; try { balance = _service.Sparkles; } catch { balance = null; }
                    foreach (var amount in DareGrammar.AmountsFor(stake))
                        tiles.Children.Add(Choice(DareSlipRules.StakeText(new DareStake(stake, amount), 0, Loc.Get), DareSlipRules.ToneOf(stake),
                            DareSlipRules.TipOf(stake), "dares-amount:" + amount, () => Press(() => DareSlipRules.PickAmount(_draft, amount, balance)),
                            DareSlipRules.AmountCovered(stake, step, amount, balance)));
                }
                break;
            case DareStep.Camera:
                foreach (var on in new[] { true, false }) tiles.Children.Add(Choice(Loc.Get(on ? "dares_tile_camera_on" : "dares_tile_camera_off"),
                    on ? DareTone.CameraOn : DareTone.CameraOff, on ? "dares_tip_camera_on" : "dares_tip_camera_off",
                    on ? "dares-camera:on" : "dares-camera:off", () => Press(() => DareSlipRules.SetCamera(_draft, on))));
                foreach (var key in new[] { "dares_cam_look", "dares_cam_pass", "dares_cam_local" })
                    _tray.Children.Add(new TextBlock { Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap });
                break;
        }
    }

    internal async Task<DareCreateOutcome?> SendAsync()
    {
        if (_sending || _draft.Who == null || DareSlipRules.ToSlip(_draft) is not { } slip) return null;
        _sending = true; Render();
        DareCreateOutcome outcome;
        try { outcome = await _service.CreateAsync(_draft.Who.Id, slip); }
        catch { outcome = new DareCreateOutcome(DareCreateStatus.TryLater); }
        _sending = false;
        if (DareCardRules.IsGood(outcome.Status)) { FriendsSfx.Sent(); _result = outcome; Render(); }
        else { FriendsSfx.Denied(); Render(); _tray.Children.Add(new TextBlock { Tag = "dares-send-result", Text = Loc.Get(DareCardRules.CreateKey(outcome.Status)), TextWrapping = TextWrapping.Wrap }); }
        return outcome;
    }

    private void RenderResult(DareCreateOutcome outcome)
    {
        _tray.Children.Add(new TextBlock { Tag = "dares-send-result", Text = Loc.Get(DareCardRules.CreateKey(outcome.Status)), TextWrapping = TextWrapping.Wrap });
        if (outcome.Status == DareCreateStatus.Writing && !string.IsNullOrEmpty(outcome.WriteCode))
        {
            var command = DareCardRules.WriteLine(outcome.WriteCode);
            _tray.Children.Add(new TextBox { Text = command, IsReadOnly = true, Tag = "dares-write-line" });
            _tray.Children.Add(Choice(Loc.Get("dares_copy"), DareTone.Who, null, "dares-copy", async () =>
            {
                try { var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard != null) await clipboard.SetTextAsync(command); }
                catch { }
            }));
        }
        _tray.Children.Add(Choice(Loc.Get("dares_close"), DareTone.Who, null, "dares-done", Close));
    }

    private int? TierOf(string id) => _tiers != null && _tiers.TryGetValue(id, out var tier) ? tier : null;
}
