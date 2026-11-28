using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;
using static ConditioningControlPanel.Avalonia.Views.Dialogs.Dares.DareSlipLook;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

// WPF 1468acc21b Dialogs/Dares/DareSlipWindow.cs:25-442. The host supplies its live service.
public sealed class DareSlipWindow : Window
{
    private static DareSlipWindow? _open;
    private readonly IDaresService _service;
    private readonly IReadOnlyList<DareFriend> _friends;
    private readonly IReadOnlyDictionary<string, int>? _tiers;
    private readonly DareDraft _draft = new();
    private readonly StackPanel _sentence = new() { MinHeight = 176 };
    private readonly StackPanel _tray = new() { Margin = new Thickness(0, 16, 0, 0), MinHeight = 150 };
    private readonly HashSet<DareStep> _filled = new();
    private readonly Border _card = new() { Tag = "dares-slip-window", Padding = new Thickness(22, 20, 22, 20) };
    private Panel? _paper;
    private bool _sending;
    private DareCreateOutcome? _result;

    internal DareDraft Draft => _draft;

    internal DareSlipWindow(IDaresService service, DareFriend? who, IReadOnlyList<DareFriend> friends,
        IReadOnlyDictionary<string, int>? tiers, bool introSeen, Action markSeen)
    {
        _service = service; _friends = friends; _tiers = tiers;
        if (who != null) { _draft.Who = who; _draft.WhoTier = TierOf(who.Id); }
        // WPF DareLook.Shell (Controls/Dares/DareLook.cs:1197-1227): a borderless violet desk, dragged anywhere off a button.
        Title = "CC Labs"; Width = 660; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false;
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.None; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new RadialGradientBrush { Center = new RelativePoint(0.3, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.3, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.15, RelativeUnit.Relative), GradientStops = { new(Color.FromRgb(0x2C, 0x1A, 0x4F), 0), new(Color.FromRgb(0x1A, 0x10, 0x30), 0.55), new(Color.FromRgb(0x14, 0x0B, 0x26), 1) } };
        PointerPressed += (_, e) => { if (e.Source is Visual v && v.FindAncestorOfType<Button>(true) == null && v.FindAncestorOfType<TextBox>(true) == null) try { BeginMoveDrag(e); } catch { } };
        TextElement.SetFontFamily(_card, PrintFont); TextElement.SetForeground(_card, Chalk);
        Content = _card;
        if (introSeen) ShowComposer();
        else _card.Child = DareIntro.Build(() => { markSeen(); ShowComposer(); });
        CardIn(_card);
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

    internal static void CloseForPanic() { try { _open?.Close(); } catch (InvalidOperationException) { } }

    // WPF DareSlipWindow.cs:89: both closes fade out first.
    private void Leave() => _ = FadeOutThen(this, Close);

    private void ShowComposer()
    {
        _card.Child = new StackPanel { Children = { Slip(SlipHead(Loc.Get("dares_slip_title"), Leave, "dares_help_numbers"), _sentence, out var paper), _tray } };
        _paper = paper; Render();
    }

    private static Button Tile(DareTone tone, string label, string? tip, string? shut, bool pressed, string tag, Action action, bool upsell = false) => DareSlipLook.Tile(
        Disc(tone, upsell), label, DareLook.ToneColor(tone), tip == null ? null : Loc.Get(tip), shut == null ? null : Loc.Get(shut), pressed, action, tag);
    private static Button Chip(string text, string? tip, string tag, Action action) => DareSlipLook.Chip(text, tip == null ? null : Loc.Get(tip), action, tag);
    private void Press(Action action) { FriendsSfx.Click(); action(); Render(); }

    private void Render()
    {
        _sentence.Children.Clear();
        var current = _result == null ? DareSlipRules.Current(_draft) : null;
        foreach (var line in DareSlipRules.Sentence(_draft, Loc.Get))
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (line.Mark != DareMark.None) row.Children.Add(Mark(line.Mark, _draft.Who?.Name));
            foreach (var seg in line.Segs)
            {
                if (seg.Kind == DareSegKind.Chip && seg.Step is { } step)
                {
                    var fill = Fill(seg.Text, seg.Tone, step == current, () => Press(() => DareSlipRules.Edit(_draft, step)), "dares-chip:" + step);
                    fill.IsHitTestVisible = _result == null;
                    if (seg.TipKey != null) ToolTip.SetTip(fill, Loc.Get(seg.TipKey));
                    row.Children.Add(fill);
                    if (!_filled.Contains(step)) PopIn(fill); // a fill that just landed pops in; the rest stay still
                }
                else row.Children.Add(seg.Kind == DareSegKind.Blank ? Blank(seg.Text, seg.Step == current) : Words(seg.Text, 26, Print));
            }
            _sentence.Children.Add(row);
        }
        _filled.Clear();
        _filled.UnionWith(Enum.GetValues<DareStep>().Where(s => DareSlipRules.IsFilled(_draft, s)));
        RenderTray();
    }

    private void RenderTray()
    {
        _tray.Children.Clear();
        if (_result != null) { RenderResult(_result); return; }
        if (DareSlipRules.Current(_draft) is not { } step)
        {
            var send = Big(Loc.Get("dares_send"), () => _ = SendAsync(), "dares-send").Also(s => s.IsEnabled = !_sending);
            var row = new WrapPanel { Children = { send } };
            if (DareSlipRules.ToSlip(_draft) is { } slip && DareSlipRules.HasDare(slip))
                row.Children.Add(Words(Loc.Get("dares_send_dare_note"), 17, Muted, wrap: true).Also(n => { n.MaxWidth = 320; n.Margin = new Thickness(12, 4, 0, 8); }));
            _tray.Children.Add(row); PopIn(send);
            return;
        }
        _tray.Children.Add(Caps(Loc.Get(DareSlipRules.AskKey(step, _draft.Type)), 15, Muted, FontWeight.Medium).Also(a => a.Margin = new Thickness(0, 0, 0, 4)));
        if (DareSlipRules.HelpFor(step, _draft) is { } help) _tray.Children.Add(Words(Loc.Get(help), 16, Muted, wrap: true).Also(h => h.Margin = new Thickness(0, 0, 0, 8)));
        var tiles = new WrapPanel { Tag = "dares-tray:" + step }; _tray.Children.Add(tiles);
        DaresMe me; try { me = _service.Snapshot.Me; } catch { me = DaresSnapshot.Dark.Me; }
        switch (step)
        {
            case DareStep.Who:
                foreach (var f in _friends) tiles.Children.Add(DareSlipLook.Tile(KnockCard.BuildAvatar(f.Name, f.AvatarUrl, 46, fallback: Initial(f.Name), allowHttps: true), f.Name, DareLook.ToneColor(DareTone.Who), f.Name, null,
                    _draft.Who?.Id == f.Id, () => Press(() => DareSlipRules.SetWho(_draft, f, TierOf(f.Id))), "dares-who:" + f.Id));
                if (_friends.Count == 0) tiles.Children.Add(Words(Loc.Get("dares_no_friends"), 18, Chalk, wrap: true));
                break;
            case DareStep.How:
                foreach (var t in new[] { DareType.Cant, DareType.Duel })
                    tiles.Children.Add(Tile(t == DareType.Duel ? DareTone.Duel : DareTone.Cant, Loc.Get(DareSlipRules.TileKey(t)), t == DareType.Duel ? "dares_tip_duel" : "dares_tip_cant",
                        null, _draft.Type == t, "dares-tile:" + DareGrammar.Wire(t), () => Press(() => DareSlipRules.SetType(_draft, t))));
                break;
            case DareStep.What:
                foreach (var k in DareSlipRules.Kinds(_draft.Type))
                    tiles.Children.Add(Tile(DareSlipRules.ToneOf(k), Loc.Get(DareSlipRules.TileKey(k)), DareSlipRules.TipOf(k), DareSlipRules.KindShut(k, me),
                        _draft.Pending is DareKind pk && pk == k, "dares-tile:" + DareGrammar.Wire(k), () => Press(() => DareSlipRules.PickKind(_draft, k))));
                if (_draft.Pending is DareKind kind)
                    AddRow("dares-targets", DareSlipRules.TargetsFor(_draft.Type, kind).Select(target => Chip(kind == DareKind.Session ? Loc.Get(DareSlipRules.TargetLabelKey(target)) : target,
                        DareSlipRules.TipOf(kind), "dares-target:" + target, () => Press(() => DareSlipRules.PickTarget(_draft, target)))));
                break;
            case DareStep.When:
                foreach (var deadline in Enum.GetValues<DareDeadline>()) tiles.Children.Add(Chip(Loc.Get(DareSlipRules.WhenKey(deadline)),
                    "dares_tip_when", "dares-when:" + DareGrammar.Wire(deadline), () => Press(() => DareSlipRules.SetDeadline(_draft, deadline))));
                break;
            case DareStep.Mine:
            case DareStep.Theirs:
                foreach (var k in DareSlipRules.StakeKinds)
                {
                    var (state, why) = DareSlipRules.StakeLock(k, step, me, TierGate.HasPremium, _draft.WhoTier);
                    var upsell = state == DareTileLock.Upsell;
                    tiles.Children.Add(Tile(DareSlipRules.ToneOf(k), Loc.Get(DareSlipRules.TileKey(k)), upsell ? why : DareSlipRules.TipOf(k), state == DareTileLock.Shut ? why : null,
                        _draft.Pending is DareStakeKind sk && sk == k, "dares-stake:" + DareGrammar.Wire(k) + (upsell ? ":locked" : ""), () =>
                        {
                            if (!upsell) { Press(() => DareSlipRules.PickStakeKind(_draft, k)); return; }
                            FriendsSfx.Click();
                            if (Owner is MainShellWindow shell) shell.BtnGateUnlock_Click(null, new RoutedEventArgs());
                        }, upsell));
                }
                if (_draft.Pending is DareStakeKind stake) AddAmounts(stake, step);
                break;
            case DareStep.Camera:
                foreach (var on in new[] { true, false }) tiles.Children.Add(Tile(on ? DareTone.CameraOn : DareTone.CameraOff, Loc.Get(on ? "dares_tile_camera_on" : "dares_tile_camera_off"),
                    on ? "dares_tip_camera_on" : "dares_tip_camera_off", null, _draft.Camera == on, on ? "dares-camera:on" : "dares-camera:off", () => Press(() => DareSlipRules.SetCamera(_draft, on))));
                _tray.Children.Add(CameraHow());
                break;
        }
    }

    // WPF DareSlipWindow.cs:252-267 and DareLook.cs:633-685: each tick represents a camera sample.
    private static Control CameraHow()
    {
        var ticks = new UniformGrid { Tag = "dares-camera-ticks", Rows = 1, Columns = 30, Height = 16,
            MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        var missed = new HashSet<int> { 4, 11, 12, 20, 27 };
        for (var i = 0; i < 30; i++)
        {
            var miss = missed.Contains(i);
            ticks.Children.Add(new Border { Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(2),
                Background = miss ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x2F, 0xD3, 0x9A)),
                BorderBrush = Muted, BorderThickness = new Thickness(miss ? 1 : 0) });
        }
        var pass = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        pass.Children.Add(ticks);
        var label = Words(Loc.Get("dares_cam_pass"), 18, Chalk).Also(t => t.Margin = new Thickness(10, 0, 0, 0));
        Grid.SetColumn(label, 1); pass.Children.Add(label);
        Control Row(IconKind icon, string key)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(Icon(icon, 24, Chalk).Also(i => i.Margin = new Thickness(0, 0, 10, 0)));
            var text = Words(Loc.Get(key), 18, Chalk, wrap: true); Grid.SetColumn(text, 1); row.Children.Add(text);
            return row;
        }
        var rows = new StackPanel { Margin = new Thickness(14, 12, 14, 12), Spacing = 10,
            Children = { Row(IconKind.Eye, "dares_cam_look"), pass, Row(IconKind.Desktop, "dares_cam_local") } };
        return new Grid { Tag = "dares-camera-how", MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0), Children = { new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
                Stroke = Muted, StrokeThickness = 1, StrokeDashArray = new() { 4, 3 }, RadiusX = 10, RadiusY = 10 }, rows } };
    }

    private void AddRow(string tag, IEnumerable<Button> chips)
    {
        var row = new WrapPanel { Tag = tag, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var chip in chips) { row.Children.Add(chip); PopIn(chip); }
        _tray.Children.Add(row);
    }

    // WPF Dialogs/Dares/DareSlipWindow.cs:225-249: an amount above the balance is greyed, never hidden, and says the balance.
    private void AddAmounts(DareStakeKind stake, DareStep side)
    {
        int? balance; try { balance = _service.Sparkles; } catch { balance = null; }
        AddRow("dares-amounts", DareGrammar.AmountsFor(stake).Select(amount =>
        {
            var chip = Chip(DareSlipRules.StakeText(new DareStake(stake, amount), 0, Loc.Get), DareSlipRules.TipOf(stake), "dares-amount:" + amount,
                () => Press(() => DareSlipRules.PickAmount(_draft, amount, balance)));
            if (DareSlipRules.AmountCovered(stake, side, amount, balance)) return chip;
            chip.Tag = "dares-amount:" + amount + ":short";
            Explain(chip, Loc.GetF("dares_sp_have", balance!.Value), true);
            return chip;
        }));
    }

    internal async Task<DareCreateOutcome?> SendAsync()
    {
        if (_sending || _draft.Who == null || DareSlipRules.ToSlip(_draft) is not { } slip) return null;
        _sending = true; Render();
        DareCreateOutcome outcome;
        try { outcome = await _service.CreateAsync(_draft.Who.Id, slip); }
        catch { outcome = new DareCreateOutcome(DareCreateStatus.TryLater); }
        _sending = false;
        if (DareCardRules.IsGood(outcome.Status)) { FriendsSfx.Sent(); _result = outcome; _paper?.Children.Add(Stamp(Loc.Get("dares_stamp_sent"))); Render(); }
        else { FriendsSfx.Denied(); Render(); _tray.Children.Add(Result(outcome, Warn)); }
        return outcome;
    }

    private static TextBlock Result(DareCreateOutcome outcome, IBrush fg) =>
        Words(Loc.Get(DareCardRules.CreateKey(outcome.Status)), 20, fg, wrap: true).Also(t => t.Tag = "dares-send-result");

    private void RenderResult(DareCreateOutcome outcome)
    {
        _tray.Children.Add(Result(outcome, Chalk));
        if (outcome.Status == DareCreateStatus.Writing && !string.IsNullOrEmpty(outcome.WriteCode))
        {
            var command = DareCardRules.WriteLine(outcome.WriteCode);
            Button? copy = null;
            copy = Chip(Loc.Get("dares_copy"), null, "dares-copy", async () =>
            {
                try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(command); FriendsSfx.Click(); ((TextBlock)((Border)copy!.Content!).Child!).Text = Loc.Get("dares_copied"); } }
                catch { } // clipboard busy; the line is still selectable
            });
            DockPanel.SetDock(copy.Also(c => c.Margin = new Thickness(10, 2, 0, 0)), Dock.Right);
            _tray.Children.Add(new DockPanel { Margin = new Thickness(0, 10, 0, 6), Children = { copy, new Border { Background = new SolidColorBrush(Color.FromArgb(0x4D, 0, 0, 0)),
                BorderBrush = Muted, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 10, 7), Child = new TextBox { Text = command,
                    IsReadOnly = true, Tag = "dares-write-line", BorderThickness = default, Background = Brushes.Transparent, Foreground = Chalk, FontFamily = new FontFamily("monospace"), FontSize = 14 } } } });
        }
        _tray.Children.Add(Chip(Loc.Get("dares_close"), null, "dares-done", Leave).Also(d => d.Margin = new Thickness(0, 10, 0, 0)));
    }

    private int? TierOf(string id) => _tiers != null && _tiers.TryGetValue(id, out var tier) ? tier : null;
}
