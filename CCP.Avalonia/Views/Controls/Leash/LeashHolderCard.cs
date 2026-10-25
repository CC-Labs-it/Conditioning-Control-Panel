// Adapted from WPF Controls/Leash/LeashHolderCard.cs at the required base commit.
// ponytail: simple figures and Fluent reward glyphs; drawn card art, LeashFx, help,
// replay snap and release confirmation remain in the row's Missing list.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using FriendsDrawerRules = ConditioningControlPanel.Controls.Friends.FriendsDrawerRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashHolderCard : Border
{
    private static readonly List<WeakReference<LeashHolderCard>> Live = new();
    internal static void CloseSheetsForPanic()
    {
        foreach (var weak in Live.ToArray())
            if (weak.TryGetTarget(out var card))
            {
                card._sheet = card._videoFor = null;
                card._resultTimer?.Stop(); card._result = null; card.Render();
            }
            else Live.Remove(weak);
    }
    private readonly Func<ILeashService?> _svc;
    private HeldLeash _h;
    private readonly StackPanel _body = new();
    private string? _sheet, _videoFor;
    private int _videoCap = LeashVideoCap.Default;
    private (string Text, bool Good)? _result;
    private DispatcherTimer? _resultTimer;
    internal string? OpenSheet => _sheet;
    internal string? ResultText => _result?.Text;
    internal DispatcherTimer? ResultTimer => _resultTimer;
    internal void ClearResult() { _resultTimer?.Stop(); _result = null; Render(); }
    internal HeldLeash Held => _h;

    internal LeashHolderCard() : this(new HeldLeash(new LeashPerson("sample", "Juno", null), false,
        LeashIntensity.Standard, DateTimeOffset.UtcNow, 1, null, null,
        Array.Empty<WeekDay>(), Array.Empty<Punishment>(), null, 0), () => null) { }

    internal LeashHolderCard(HeldLeash held, Func<ILeashService?> service)
    {
        _h = held; _svc = service;
        Live.RemoveAll(w => !w.TryGetTarget(out _));
        Live.Add(new WeakReference<LeashHolderCard>(this));
        Tag = "leash-holder-card:" + held.Who.Id;
        Background = LeashAskCard.Card; BorderBrush = LeashAskCard.GoldDeep;
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12); Margin = new Thickness(0, 4, 0, 6); Child = _body;
        Render();
    }

    internal void Update(HeldLeash held) { _h = held; Render(); }
    private bool ReceiptsOn { get { try { return _svc()?.ReceiptsSupported == true; } catch { return false; } } }

    internal void Render()
    {
        _body.Children.Clear();
        var dnd = LeashUiRules.DndOn(_h.DndUntil, DateTimeOffset.UtcNow);
        _body.Children.Add(Label(_h.Who.Name, F.Text));
        _body.Children.Add(Label(Loc.GetF("leash_holder_day", _h.Day), F.Gold));
        _body.Children.Add(Label(Loc.Get(dnd ? "leash_state_quiet" : _h.Online ? "leash_state_online" : "leash_state_offline"), F.Muted));
        var r = _h.Report;
        var figures = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        TextBlock Figure(string value, string key) { var t = Label(value + " · " + Loc.Get(key), F.Text); figures.Children.Add(t); return t; }
        Figure(r == null ? "-" : r.Minutes.ToString(), "leash_fig_minutes");
        Figure(r == null ? "-" : $"{r.QuestsDone}/{r.QuestsTotal}", "leash_fig_quests");
        if (r is { ChasterLinked: true })
        {
            var lockFig = Figure(r.LockLeftSeconds is int seconds ? LeashUiRules.LockLeft(seconds) : "-", "leash_fig_lock");
            if (r.TabSeconds is int tab)   // WPF LeashHolderCard.cs:335-337: the tab line sits under the lock figure
            {
                figures.Children.Remove(lockFig);
                var tabLine = Label(Loc.GetF("leash_fig_tab", LeashUiRules.Tab(tab)), tab > 0 ? F.Red : F.Mint);
                tabLine.Tag = "leash-fig-tab";
                figures.Children.Add(new StackPanel { Tag = "leash-fig-lock", Children = { lockFig, tabLine } });
            }
        }
        else Figure(r == null ? "-" : r.Streak.ToString(), "leash_fig_streak_label");
        _body.Children.Add(figures);
        if (_h.Assignment is { } a)
            _body.Children.Add(Label(Loc.GetF(a.Status switch { AssignStatus.Done => "leash_task_done", AssignStatus.Missed => "leash_task_missed", _ => "leash_task_open" }, AssignText(a.Kind, a.Size, a.Watch)), F.Gold));
        var week = new UniformGrid { Columns = 7, Tag = "leash-week" };
        foreach (var day in _h.Week.TakeLast(7))
            week.Children.Add(WeekCell(day));
        _body.Children.Add(week);
        if (dnd && _h.DndUntil is { } until) _body.Children.Add(Label(Loc.GetF("leash_holder_dnd", _h.Who.Name, LeashUiRules.DndUntilText(until)), F.Lilac));
        var buttons = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var id in new[] { "assign", "reward", "punish", "tug" })
        {
            var b = F.Pill(Loc.Get("leash_btn_" + id), F.Raised, F.Text, "leash-btn:" + id + (_sheet == id ? ":open" : ""), F.Line2);
            b.IsEnabled = id == "reward" || !dnd;
            b.Click += async (_, _) => { if (id == "tug") await TugAsync(); else ToggleSheet(id); };
            buttons.Children.Add(b);
        }
        _body.Children.Add(buttons);
        if (_sheet != null) _body.Children.Add(_sheet switch { "assign" => AssignSheet(), "reward" => RewardSheet(), _ => PunishSheet() });
        if (_result is { } result) { var line = Label(result.Text, result.Good ? F.Mint : F.Gold); line.Tag = "leash-result"; _body.Children.Add(line); }
        if (ReceiptsOn)
        {
            try
            {
                var items = _svc()?.SentTo(_h.Who.Id) ?? Array.Empty<LeashSentItem>();
                if (items.Count > 0) _body.Children.Add(Label(Loc.Get("leash_sent_title"), F.Muted));
                foreach (var item in items.Take(LeashUiRules.SentRows))
                {
                    var line = Label(LeashUiRules.ItemText(item, Loc.Get) + " · " + Loc.Get(LeashUiRules.StepKey(item.Step)) + " · " + LeashUiRules.StepTime(item.StepAt, DateTimeOffset.UtcNow), F.Muted);
                    line.Tag = "leash-sent-row:" + item.Kind.ToString().ToLowerInvariant() + ":" + item.Step.ToString().ToLowerInvariant();
                    ToolTip.SetTip(line, LeashUiRules.Timeline(item, DateTimeOffset.UtcNow, Loc.Get));
                    _body.Children.Add(line);
                }
            }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] receipt repaint failed: {E}", ex.Message); }
        }
    }
    internal void ToggleSheet(string id) { _sheet = _sheet == id ? null : id; _videoFor = null; Render(); }
    private static string AssignText(AssignKind kind, int size, LeashWatch? watch) => kind == AssignKind.Video ? Loc.Get("leash_assign_video_n") : Loc.GetF(LeashUiRules.AssignSizeKey(kind), size);
    private static TextBlock Label(string text, IBrush color) => new() { Text = text, Foreground = color, FontFamily = F.Display, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 3) };
    private static readonly IBrush IdleDot = new SolidColorBrush(Color.FromRgb(0x4A, 0x3D, 0x61));   // WPF LeashLook.Idle
    // WPF LeashLook.WeekCell (LeashLook.cs:380-405): a dot per mark over the day letter, never the enum name.
    private static StackPanel WeekCell(WeekDay day)
    {
        var dot = new Ellipse { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Center };
        switch (day.Mark)
        {
            case WeekMark.Did: dot.Fill = F.Mint; break;
            case WeekMark.Punished: dot.Fill = F.Red; break;
            case WeekMark.Today: dot.Fill = Brushes.Transparent; dot.Stroke = F.Gold; dot.StrokeThickness = 2; dot.StrokeDashArray = new AvaloniaList<double> { 1.6, 1.2 }; break;
            default: dot.Fill = IdleDot; break;
        }
        var letter = new TextBlock { Text = LeashUiRules.DayLetter(day.Day), FontSize = 9.5, FontWeight = FontWeight.Bold, Foreground = F.Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        return new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Tag = "leash-week:" + day.Mark, Children = { dot, letter } };
    }
    private static TextBlock Wrap(TextBlock label) { label.TextWrapping = TextWrapping.Wrap; return label; }
    // ---- sheets -----------------------------------------------------------------------

    private Border Sheet(string title, string tag)
    {
        var b = new Border
        {
            Background = F.Raised,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Tag = tag,
        };
        var sp = new StackPanel();
        var head = Label(title, F.Muted);
        head.Margin = new Thickness(0, 0, 0, 6);
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(head);
        b.Child = sp;
        return b;
    }

    private static StackPanel Rows(Border sheet) => (StackPanel)sheet.Child!;

    /// <summary>One sheet row: a name on the left and its size chips on the right.</summary>
    private Control SizeRow(string label, string icon, IBrush accent, IEnumerable<(string Text, string Tag, Func<Task> Send)> chips)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = new IconGlyph { Kind = icon switch { "clock" => IconKind.Clock, "lock" => IconKind.LockClosed, "eye" => IconKind.Eye, "bubble" => IconKind.BubbleMultiple, "play" => IconKind.Play, "heart" => IconKind.Heart, "scissors" => IconKind.Cut, _ => IconKind.Flash }, Foreground = accent, Size = 13 };
        ic.Margin = new Thickness(0, 0, 6, 0);
        name.Children.Add(ic);
        name.Children.Add(F.Label(label, 12.5, F.Text, F.Display, FontWeight.Medium));
        g.Children.Add(name);
        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, tag, send) in chips)
        {
            var c = F.Pill(new TextBlock { Text = text, FontFamily = F.Mono, FontSize = 11.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                F.Raised, F.Text, "leash-size", F.Line2);
            c.Padding = new Thickness(7, 4); c.CornerRadius = new CornerRadius(8);
            c.MinWidth = 40;
            c.Margin = new Thickness(4, 0, 0, 0);
            c.Tag = tag;
            c.Click += async (_, _) =>
            {
                await send();
            };
            chipsPanel.Children.Add(c);
        }
        Grid.SetColumn(chipsPanel, 1);
        g.Children.Add(chipsPanel);
        return g;
    }

    private Control PunishSheet()
    {
        var levelName = Loc.Get(LeashUiRules.Key(_h.Intensity));
        var sheet = Sheet(Loc.GetF("leash_sheet_punish", Math.Min(_h.PunishedToday, 3), levelName), "leash-sheet:punish");
        var rows = Rows(sheet);
        if (_h.PunishedToday >= 3)
        {
            rows.Children.Add(Wrap(F.Label(Loc.Get("leash_sheet_punish_cap"), 12, F.Muted)));
            return sheet;
        }
        if (_h.Pending.Count > 0)
        {
            var w = Wrap(F.Label(Loc.GetF("leash_sheet_waiting", _h.Pending.Count), 11, F.Gold));
            w.Margin = new Thickness(0, 0, 0, 4);
            w.Tag = "leash-sheet-waiting";
            rows.Children.Add(w);
        }
        foreach (var k in LeashUiRules.VisiblePunishments(_h.Intensity, _h.Report?.ChasterLinked == true))
        {
            var kind = k;
            var chips = LeashUiRules.Sizes(k).Select(sz => (
                k == PunishKind.Video ? Loc.Get("leash_pick") : LeashUiRules.SizeText(k, sz),
                $"leash-punish:{k.ToString().ToLowerInvariant()}:{sz}",
                (Func<Task>)(() => k == PunishKind.Video ? OpenVideo("punish") : SendPunishAsync(kind, sz, null))));
            rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), PunishIcon(k), F.Red, chips));
            if (k == PunishKind.Video && _videoFor == "punish")
            {
                _videoCap = Math.Min(_videoCap, _h.VideoMax);
                var cap = new LeashCapSlider(Loc.Get("leash_video_cap_label"), _videoCap, "leash-video-cap")
                {
                    Ceiling = _h.VideoMax,
                    Margin = new Thickness(20, 2, 0, 6),

                };
                ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
                cap.Committed += v => _videoCap = v;
                rows.Children.Add(cap);
                rows.Children.Add(VideoPicker(w => SendPunishAsync(PunishKind.Video, _videoCap, w)));
            }
        }
        return sheet;
    }

    private Control AssignSheet()
    {
        var sheet = Sheet(Loc.GetF("leash_sheet_assign", _h.Who.Name), "leash-sheet:assign");
        var rows = Rows(sheet);
        if (_h.Assignment is { Status: AssignStatus.Open } a)
        {
            var w = Wrap(F.Label(Loc.GetF("leash_sheet_replaces", AssignText(a.Kind, a.Size, a.Watch)), 11, F.Gold));
            w.Margin = new Thickness(0, 0, 0, 4);
            rows.Children.Add(w);
        }
        foreach (var k in LeashUiRules.AssignOrder)
        {
            var kind = k;
            var chips = LeashUiRules.Sizes(k).Select(sz => (
                k == AssignKind.Video ? Loc.Get("leash_pick") : sz.ToString(),
                $"leash-assign:{k.ToString().ToLowerInvariant()}:{sz}",
                (Func<Task>)(() => k == AssignKind.Video ? OpenVideo("assign") : SendAssignAsync(kind, sz, null))));
            rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), k switch { AssignKind.Minutes => "clock", AssignKind.Quests => "task", _ => "play" },
                F.Gold, chips));
            if (k == AssignKind.Video && _videoFor == "assign") rows.Children.Add(VideoPicker(w => SendAssignAsync(AssignKind.Video, 1, w)));
        }
        return sheet;
    }

    private Control RewardSheet()
    {
        var sheet = Sheet(Loc.Get("leash_sheet_reward"), "leash-sheet:reward");
        var rows = Rows(sheet);

        // Stickers: the discs themselves are the buttons.
        var stickers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        foreach (var id in LeashUiRules.Stickers)
        {
            var sid = id;
            var disc = F.GlyphContent(IconKind.Star, Loc.Get("leash_rew_sticker_" + id));
            var b = F.Pill(disc, Brushes.Transparent, F.Text, "leash-sticker", Brushes.Transparent);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Tag = "leash-reward:sticker:" + id;
            ToolTip.SetTip(b, Loc.Get("leash_rew_sticker_" + id));
            b.Click += async (_, _) =>
            {
                await SendRewardAsync(RewardKind.Sticker, sid, null);
            };
            stickers.Children.Add(b);
        }
        rows.Children.Add(stickers);

        rows.Children.Add(SizeRow(Loc.Get("leash_rew_praise"), "heart", F.Pink,
            LeashUiRules.Praise.Select(p => (Loc.Get("leash_praise_" + p), "leash-reward:praise:" + p, (Func<Task>)(() => SendRewardAsync(RewardKind.Praise, p, null))))));
        rows.Children.Add(SizeRow(Loc.Get("leash_rew_pardon"), "scissors", F.Lilac,
            new[] { (Loc.Get("leash_give"), "leash-reward:pardon", (Func<Task>)(() => SendRewardAsync(RewardKind.Pardon, null, null))) }));
        if (_h.Report?.ChasterLinked == true)
        {
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_credit"), "lock", F.Mint,
                LeashUiRules.CreditSizes.Select(s => ("-" + LeashUiRules.Clock(s), "leash-reward:credit:" + s, (Func<Task>)(() => SendRewardAsync(RewardKind.Credit, null, s))))));
        }
        return sheet;
    }

    private static string PunishIcon(PunishKind k) => k switch
    {
        PunishKind.Lines => "lock",
        PunishKind.Pink => "eye",
        PunishKind.Bubbles => "bubble",
        PunishKind.Detention => "clock",
        PunishKind.Video => "play",
        _ => "bolt",
    };

    private Task OpenVideo(string forWhat)
    {
        _videoFor = _videoFor == forWhat ? null : forWhat;
        Render();
        return Task.CompletedTask;
    }

    /// <summary>The video picker: the friends watch grammar (catalogue entries, or a Hypnotube
    /// number). The number box filters to digits as it is typed; nothing else crosses the wire.</summary>
    private Control VideoPicker(Func<LeashWatch, Task> send)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 2, 0, 6), Tag = "leash-video-picker" };
        foreach (var (id, title) in FriendsInviteCodes.CatalogueWatches().Take(5))
        {
            var b = F.Pill(title, F.Raised, F.Text, "leash-catalogue", F.Line2);
            b.Padding = new Thickness(8, 4); b.CornerRadius = new CornerRadius(8);
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 0, 3);
            b.Click += async (_, _) => await send(new LeashWatch("catalogue", id, title));
            sp.Children.Add(b);
        }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            FontFamily = F.Mono,
            FontSize = 12.5,
            Foreground = F.Text,
            Background = F.Raised,
            BorderBrush = F.Line2,
            CaretBrush = F.Lilac,
            Padding = new Thickness(6, 3, 6, 3),

            Tag = "leash-ht-box",
        };
        ToolTip.SetTip(box, Loc.Get("leash_video_ht_hint"));
        var go = F.Pill(Loc.Get("leash_send"), F.Gold, LeashAskCard.Ink, "leash-video-send", F.Gold);
        go.Padding = new Thickness(10, 3); go.CornerRadius = new CornerRadius(8);
        go.Margin = new Thickness(6, 0, 0, 0);
        go.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            go.IsEnabled = n.Length > 0;
        };
        go.Click += async (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length > 0) await send(new LeashWatch("ht", id, null));
        };
        g.Children.Add(box);
        Grid.SetColumn(go, 1);
        g.Children.Add(go);
        sp.Children.Add(g);
        return sp;
    }

    // ---- sends ------------------------------------------------------------------------

    internal async Task<LeashSendStatus> SendPunishAsync(PunishKind k, int size, LeashWatch? w)
    {
        var r = await Guarded(s => s.PunishAsync(_h.Who.Id, k, size, w));
        if (LeashUiRules.IsGood(r.Status)) { _sheet = null; _videoFor = null; }
        Word(r, Loc.GetF(r.Status == LeashSendStatus.Queued ? "leash_done_punish_queued" : "leash_done_punish", _h.Who.Name));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendAssignAsync(AssignKind k, int size, LeashWatch? w)
    {
        var r = await Guarded(s => s.AssignAsync(_h.Who.Id, k, size, w));
        if (LeashUiRules.IsGood(r.Status)) { _sheet = null; _videoFor = null; }
        Word(r, Honest("leash_done_assign"));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendRewardAsync(RewardKind k, string? stickerOrPoke, int? size)
    {
        var r = await Guarded(s => s.RewardAsync(_h.Who.Id, k, stickerOrPoke, size));
        if (LeashUiRules.IsGood(r.Status)) _sheet = null;
        Word(r, Honest("leash_done_reward"));
        return r.Status;
    }

    /// <summary>The line after a good send. With receipts the steps below say when they see it,
    /// so the line only says it went; without them it keeps its old wording.</summary>
    private string Honest(string oldKey) => ReceiptsOn ? Loc.Get("leash_res_sent") : Loc.GetF(oldKey, _h.Who.Name);

    /// <summary>One tug per friend every ten seconds, answered here before the wire.</summary>
    private static readonly LeashTugThrottle TugThrottle = new();

    internal async Task<LeashSendStatus> TugAsync()
    {
        if (!TugThrottle.TryTug(_h.Who.Id, DateTime.UtcNow))
        {
            var slow = new LeashSendResult(LeashSendStatus.TooFast);
            Word(slow, Loc.GetF("leash_done_tug", _h.Who.Name));
            return slow.Status;
        }
        var r = await Guarded(s => s.TugAsync(_h.Who.Id));
        Word(r, Honest("leash_done_tug"));
        return r.Status;
    }

    private async Task<LeashSendResult> Guarded(Func<ILeashService, Task<LeashSendResult>> call)
    {
        try
        {
            var s = _svc();
            if (s == null) return new LeashSendResult(LeashSendStatus.Off);
            var r = await call(s);
            return r;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] send failed: {E}", ex.Message);
            return new LeashSendResult(LeashSendStatus.Failed);
        }
    }

    private void Word(LeashSendResult r, string goodText)
    {
        bool good = LeashUiRules.IsGood(r.Status);
        string text = good ? goodText
            : r.Status == LeashSendStatus.Dnd && r.DndUntil is { } u ? Loc.GetF("leash_res_dnd_until", _h.Who.Name, LeashUiRules.DndUntilText(u))
            : Loc.Get(LeashUiRules.ResultKey(r.Status));
        _result = (text, good);
        _resultTimer?.Stop();
        _resultTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _resultTimer.Tick += (_, _) => ClearResult();
        _resultTimer.Start();
        Render();
    }
}
