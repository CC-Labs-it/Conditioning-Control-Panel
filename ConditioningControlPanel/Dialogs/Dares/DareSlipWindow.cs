using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Dialogs.Dares;

/// <summary>
/// THE SLIP: one sentence that writes itself one blank at a time (DARES-HANDOFF "The slip"), on the
/// pink betting slip of the owner's mockup. Only the blank in hand shows its options in the tray
/// under the slip; a filled blank is a handwritten fill in its kind's colour, and a tap on it opens
/// that blank again. Send hands the slip to <c>App.Dares.CreateAsync</c>; a slip with a dare on it
/// comes back as Writing with the <c>/bet write</c> line to copy into Discord.
/// The rules are <see cref="DareSlipRules"/>; this window only draws and forwards presses.
/// </summary>
public sealed class DareSlipWindow : Window
{
    private static DareSlipWindow? _open;

    private readonly IDaresService _svc;
    private readonly IReadOnlyList<DareFriend> _friends;
    private readonly DareDraft _d = new();
    private readonly Border _card;
    private readonly StackPanel _body = new();
    private readonly Grid _slip;
    private readonly Grid _paper;
    private readonly StackPanel _sentence = new() { MinHeight = 176 };
    private readonly StackPanel _tray = new() { Margin = new Thickness(0, 16, 0, 0), MinHeight = 150 };
    private readonly HashSet<DareStep> _filled = new();
    private bool _sending;
    private DareCreateOutcome? _result;

    private readonly IReadOnlyDictionary<string, int>? _tiers;

    private DareSlipWindow(IDaresService svc, DareFriend? who, IReadOnlyList<DareFriend> friends, IReadOnlyDictionary<string, int>? tiers)
    {
        _svc = svc;
        _friends = friends;
        _tiers = tiers;
        if (who != null) { _d.Who = who; _d.WhoTier = TierOf(who.Id); }
        _card = DareLook.Shell(this, 660);
        _card.Tag = "dares-slip-window";
        _slip = DareLook.Slip(DareLook.SlipHead(Loc.Get("dares_slip_title"), "dares_help_numbers", Leave), _sentence, out _paper);

        if (DareIntro.Seen()) ShowComposer();
        else _card.Child = DareIntro.Build(ShowComposer);
        DareLook.CardIn(_card);
    }

    /// <summary>Opens the slip, the friend filled in when the door named one. Does nothing while the
    /// server is dark. One slip at a time: a second open replaces the first.</summary>
    public static void Open(Window? owner, DareFriend? who, IReadOnlyList<DareFriend> friends,
        IReadOnlyDictionary<string, int>? tiers = null)
    {
        var svc = App.Dares;
        if (svc == null) return;
        try { if (!svc.Snapshot.Available) return; } catch { return; }
        try { _open?.Close(); } catch (InvalidOperationException) { } // swallow: already closing
        var w = new DareSlipWindow(svc, who, friends, tiers);
        try { if (owner is { IsVisible: true }) w.Owner = owner; }
        catch (InvalidOperationException) { } // swallow: owner closing; the slip stands alone
        _open = w;
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        w.Show();
    }

    /// <summary>For the suite: the draft behind the window.</summary>
    internal DareDraft Draft => _d;

    private void ShowComposer()
    {
        _body.Children.Clear();
        _body.Children.Add(_slip);
        _body.Children.Add(_tray);
        _card.Child = _body;
        Render();
    }

    private void Leave() => DareLook.FadeOutThen(this, Close);

    private void Render()
    {
        var cur = _result == null ? DareSlipRules.Current(_d) : null;
        _sentence.Children.Clear();
        var lines = DareSlipRules.Sentence(_d, Loc.Get);
        var view = DareLook.Sentence(lines, _d.Who?.Name, cur, _result == null ? OnChip : null);
        _sentence.Children.Add(view);
        PopNewChips(view);
        RenderTray(cur);
    }

    /// <summary>A fill that just landed pops in; the rest stay still.</summary>
    private void PopNewChips(FrameworkElement view)
    {
        var now = new HashSet<DareStep>();
        foreach (var s in Enum.GetValues<DareStep>()) if (DareSlipRules.IsFilled(_d, s)) now.Add(s);
        foreach (var el in Walk(view))
        {
            if (el.Tag is not string tag || !tag.StartsWith("dares-chip:", StringComparison.Ordinal)) continue;
            if (Enum.TryParse<DareStep>(tag.Substring("dares-chip:".Length), out var step) && !_filled.Contains(step))
                DareLook.PopIn(el);
        }
        _filled.Clear();
        _filled.UnionWith(now);
    }

    private static IEnumerable<FrameworkElement> Walk(FrameworkElement root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is FrameworkElement fe) yield return fe;
            if (d is Panel p) foreach (UIElement c in p.Children) stack.Push(c);
            else if (d is Decorator dec && dec.Child != null) stack.Push(dec.Child);
        }
    }

    private void OnChip(DareStep s)
    {
        FriendsSfx.Click();
        DareSlipRules.Edit(_d, s);
        Render();
    }

    private void Press(Action change)
    {
        FriendsSfx.Click();
        change();
        Render();
    }

    // ---- the tray ---------------------------------------------------------------------

    private void RenderTray(DareStep? cur)
    {
        _tray.Children.Clear();
        if (_result != null) { RenderResult(); return; }
        if (cur == null) { RenderSend(); return; }

        var step = cur.Value;
        _tray.Children.Add(DareLook.Ask(Loc.Get(DareSlipRules.AskKey(step, _d.Type)), DareSlipRules.HelpFor(step, _d)));

        var tiles = new WrapPanel { Tag = "dares-tray:" + step };
        _tray.Children.Add(tiles);
        var me = Me();
        switch (step)
        {
            case DareStep.Who:
                foreach (var f in _friends) tiles.Children.Add(FriendTile(f));
                if (_friends.Count == 0) tiles.Children.Add(DareLook.Words(Loc.Get("dares_no_friends"), 18, DareLook.ChalkBrush, wrap: true));
                break;
            case DareStep.How:
                foreach (var t in new[] { DareType.Cant, DareType.Duel })
                    tiles.Children.Add(DareLook.Tile(t == DareType.Duel ? DareTone.Duel : DareTone.Cant, Loc.Get(DareSlipRules.TileKey(t)),
                        t == DareType.Duel ? "dares_tip_duel" : "dares_tip_cant", null, _d.Type == t,
                        () => Press(() => DareSlipRules.SetType(_d, t)), "dares-tile:" + DareGrammar.Wire(t)));
                break;
            case DareStep.What:
                foreach (var k in DareSlipRules.Kinds(_d.Type))
                    tiles.Children.Add(DareLook.Tile(DareSlipRules.ToneOf(k), Loc.Get(DareSlipRules.TileKey(k)), DareSlipRules.TipOf(k),
                        DareSlipRules.KindShut(k, me), _d.Pending is DareKind pk && pk == k,
                        () => Press(() => DareSlipRules.PickKind(_d, k)), "dares-tile:" + DareGrammar.Wire(k)));
                if (_d.Pending is DareKind pending) AddTargets(pending);
                break;
            case DareStep.When:
                foreach (var w in Enum.GetValues<DareDeadline>())
                    tiles.Children.Add(DareLook.Choice(Loc.Get(DareSlipRules.WhenKey(w)), Loc.Get("dares_tip_when"),
                        () => Press(() => DareSlipRules.SetDeadline(_d, w)), "dares-when:" + DareGrammar.Wire(w)));
                break;
            case DareStep.Mine:
            case DareStep.Theirs:
                foreach (var k in DareSlipRules.StakeKinds)
                {
                    var (lk, why) = DareSlipRules.StakeLock(k, step, me, IAmSub(), _d.WhoTier);
                    var upsell = lk == DareTileLock.Upsell;
                    tiles.Children.Add(DareLook.Tile(DareSlipRules.ToneOf(k), Loc.Get(DareSlipRules.TileKey(k)),
                        upsell ? why : DareSlipRules.TipOf(k), lk == DareTileLock.Shut ? why : null,
                        _d.Pending is DareStakeKind sk && sk == k,
                        upsell ? OpenUpgrade : () => Press(() => DareSlipRules.PickStakeKind(_d, k)),
                        "dares-stake:" + DareGrammar.Wire(k), upsell));
                }
                if (_d.Pending is DareStakeKind stake) AddAmounts(stake);
                break;
            default:
                tiles.Children.Add(DareLook.Tile(DareTone.CameraOn, Loc.Get("dares_tile_camera_on"), "dares_tip_camera_on", null,
                    _d.Camera == true, () => Press(() => DareSlipRules.SetCamera(_d, true)), "dares-camera:on"));
                tiles.Children.Add(DareLook.Tile(DareTone.CameraOff, Loc.Get("dares_tile_camera_off"), "dares_tip_camera_off", null,
                    _d.Camera == false, () => Press(() => DareSlipRules.SetCamera(_d, false)), "dares-camera:off"));
                _tray.Children.Add(CameraHow());
                break;
        }
    }

    private void AddTargets(DareKind k)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 0), Tag = "dares-targets" };
        foreach (var t in DareSlipRules.TargetsFor(_d.Type, k))
        {
            var label = k == DareKind.Session ? Loc.Get(DareSlipRules.TargetLabelKey(t)) : t;
            var target = t;
            var chip = DareLook.Choice(label, Loc.Get(DareSlipRules.TipOf(k)), () => Press(() => DareSlipRules.PickTarget(_d, target)), "dares-target:" + t);
            row.Children.Add(chip);
            DareLook.PopIn(chip);
        }
        _tray.Children.Add(row);
    }

    private void AddAmounts(DareStakeKind k)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 0), Tag = "dares-amounts" };
        foreach (var a in DareGrammar.AmountsFor(k))
        {
            var amount = a;
            var chip = DareLook.Choice(DareSlipRules.StakeText(new DareStake(k, a), 0, Loc.Get), Loc.Get(DareSlipRules.TipOf(k)),
                () => Press(() => DareSlipRules.PickAmount(_d, amount)), "dares-amount:" + a);
            row.Children.Add(chip);
            DareLook.PopIn(chip);
        }
        _tray.Children.Add(row);
    }

    private Button FriendTile(DareFriend f)
        => DareLook.FaceTile(f.Name, f.AvatarUrl, _d.Who?.Id == f.Id,
            () => Press(() => DareSlipRules.SetWho(_d, f, TierOf(f.Id))), "dares-who:" + f.Id);

    /// <summary>The camera explained in pictures: we look a few times a second and add up the time;
    /// seen 80% = pass; it runs on this PC; only the seconds leave it.</summary>
    private static FrameworkElement CameraHow()
    {
        var pass = new Grid();
        pass.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pass.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ticks = DareLook.Ticks(30, new HashSet<int> { 4, 11, 12, 20, 27 });
        ticks.MinWidth = 150;
        pass.Children.Add(ticks);
        var label = DareLook.Words(Loc.Get("dares_cam_pass"), 18, DareLook.ChalkBrush);
        label.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(label, 1);
        pass.Children.Add(label);
        return DareLook.HowBox(
            DareLook.HowRow(DareLook.Icon("eye", 24, DareLook.ChalkBrush), Loc.Get("dares_cam_look")),
            pass,
            DareLook.HowRow(DareLook.Icon("pc", 24, DareLook.ChalkBrush), Loc.Get("dares_cam_local")));
    }

    // ---- send -------------------------------------------------------------------------

    private void RenderSend()
    {
        var row = new WrapPanel();
        var send = DareLook.Big(Loc.Get("dares_send"), DareBig.Hot, () => _ = SendAsync(), "dares-send");
        send.IsEnabled = !_sending;
        row.Children.Add(send);
        if (DareSlipRules.ToSlip(_d) is { } slip && DareSlipRules.HasDare(slip))
        {
            var note = DareLook.Words(Loc.Get("dares_send_dare_note"), 17, DareLook.MutedBrush, wrap: true);
            note.MaxWidth = 320;
            note.Margin = new Thickness(4, 12, 0, 8);
            note.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(note);
        }
        _tray.Children.Add(row);
        DareLook.PopIn(send);
    }

    /// <summary>Sends the slip and words the answer. Internal for the suite.</summary>
    internal async Task<DareCreateOutcome?> SendAsync()
    {
        if (_sending || _d.Who == null || DareSlipRules.ToSlip(_d) is not { } slip) return null;
        _sending = true;
        Render();
        DareCreateOutcome r;
        try { r = await _svc.CreateAsync(_d.Who.Id, slip); }
        catch { r = new DareCreateOutcome(DareCreateStatus.TryLater); }
        _sending = false;
        if (DareCardRules.IsGood(r.Status))
        {
            FriendsSfx.Sent();
            _result = r;
            _paper.Children.Add(DareLook.Stamp(Loc.Get("dares_stamp_sent")));
            Render();
        }
        else
        {
            FriendsSfx.Denied();
            Render();
            var why = DareLook.Words(Loc.Get(DareCardRules.CreateKey(r.Status)), 18, DareLook.WarnBrush, wrap: true);
            why.Tag = "dares-send-result";
            _tray.Children.Add(why);
        }
        return r;
    }

    private void RenderResult()
    {
        var r = _result!;
        var line = DareLook.Words(Loc.Get(DareCardRules.CreateKey(r.Status)), 21, DareLook.ChalkBrush, wrap: true);
        line.Tag = "dares-send-result";
        _tray.Children.Add(line);
        if (r.Status == DareCreateStatus.Writing && !string.IsNullOrEmpty(r.WriteCode))
        {
            var cmd = DareCardRules.WriteLine(r.WriteCode!);
            var g = new Grid { Margin = new Thickness(0, 10, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new TextBox
            {
                Text = cmd,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = DareLook.ChalkBrush,
                FontFamily = FriendsLook.Mono,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "dares-write-line",
            };
            AutomationProperties.SetName(text, cmd);
            var box = new Border
            {
                Background = DareLook.Tint(Colors.Black, 0x4D),
                BorderBrush = DareLook.MutedBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                VerticalAlignment = VerticalAlignment.Top,
                Child = text,
            };
            g.Children.Add(box);
            var copied = Loc.Get("dares_copied");
            Button? copy = null;
            copy = DareLook.Choice(Loc.Get("dares_copy"), null, () =>
            {
                try { Clipboard.SetText(cmd); DareLook.SetChoiceText(copy!, copied); FriendsSfx.Click(); }
                catch { /* clipboard busy; the line is still selectable */ }
            }, "dares-copy");
            copy.Margin = new Thickness(10, 2, 0, 0);
            Grid.SetColumn(copy, 1);
            g.Children.Add(copy);
            _tray.Children.Add(g);
        }
        var done = DareLook.Quiet(Loc.Get("dares_close"), null, Leave, "dares-done");
        done.Margin = new Thickness(0, 10, 0, 0);
        _tray.Children.Add(done);
    }

    /// <summary>Basic or Prime on this account (a Lockdown stake on my side needs it).</summary>
    internal static Func<bool> IAmSub { get; set; } = () => Services.TierGate.HasPremium;

    /// <summary>The locked Lockdown tile's door: the app's own upgrade card for locked features
    /// (Reconnect instead for a patron whose grant died here, as every gate does).</summary>
    private void OpenUpgrade()
    {
        FriendsSfx.Click();
        try
        {
            var mw = App.MainWindowRef;
            if (mw == null) return;
            if (Services.TierGate.ReconnectIsTheAnswer()) mw.StartPatreonReconnectFromGate();
            else mw.ShowVaultGate(null, 1);
        }
        catch (Exception ex) { App.Logger?.Debug("[Dares] upgrade door: {E}", ex.Message); }
    }

    private int? TierOf(string friendId) => _tiers != null && _tiers.TryGetValue(friendId, out var t) ? t : null;

    private DaresMe Me()
    {
        try { return _svc.Snapshot.Me; } catch { return DaresSnapshot.Dark.Me; }
    }
}
