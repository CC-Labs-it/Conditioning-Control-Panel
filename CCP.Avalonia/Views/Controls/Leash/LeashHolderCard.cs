// Adapted from WPF Controls/Leash/LeashHolderCard.cs at 67181df5b4b5695ee5c30bc407f715daf142e3f7.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>Holder tools shared by the page and friends drawer. Open sheets survive a poll.</summary>
public sealed class LeashHolderCard : Border
{
    internal static readonly IBrush Shade = new SolidColorBrush(Color.Parse("#241838"));
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _body = new();
    private HeldLeash _held;
    private readonly bool _readOnly;
    private string? _sheet, _videoFor;
    private int _videoCap = LeashVideoCap.Default;
    private (string Text, bool Good)? _result;
    private DispatcherTimer? _resultTimer;
    private DateTimeOffset _resultUntil;
    private static readonly LeashTugThrottle TugThrottle = new();

    // ponytail: avatar tag, minutes ring, sticker discs, replay/help and LeashFx remain in the ledger.
    internal LeashHolderCard() : this(new HeldLeash(new LeashPerson("sample", "Juno", null), true,
        LeashIntensity.Standard, DateTimeOffset.UtcNow.AddDays(-2), 3, null, null,
        Array.Empty<WeekDay>(), Array.Empty<Punishment>(), null, 0), () => null) { }

    internal LeashHolderCard(HeldLeash held, Func<ILeashService?> service, bool readOnly = false)
    {
        _held = held;
        _readOnly = readOnly;
        _service = service;
        Tag = "leash-holder-card:" + held.Who.Id;
        Background = LeashAskCard.Card;
        BorderBrush = LeashAskCard.GoldDeep;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Child = _body;
        DetachedFromVisualTree += (_, _) => _resultTimer?.Stop();
        AttachedToVisualTree += (_, _) =>
        {
            if (_result == null || _resultTimer == null) return;
            var remaining = _resultUntil - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) { _result = null; Render(); }
            else { _resultTimer.Interval = remaining; _resultTimer.Start(); }
        };
        Render();
    }

    internal string? OpenSheet => _sheet;
    internal string? ResultText => _result?.Text;
    internal void Update(HeldLeash held) { _held = held; Render(); }
    private bool ReceiptsOn { get { try { return _service()?.ReceiptsSupported == true; } catch { return false; } } }

    internal void Render()
    {
        _body.Children.Clear();
        bool dnd = LeashUiRules.DndOn(_held.DndUntil, DateTimeOffset.UtcNow);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var av = F.Avatar(_held.Who.Name, 46, _held.Online);
        av.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(av);
        var who = new StackPanel();
        who.Children.Add(Text(_held.Who.Name, 18, F.Text));
        who.Children.Add(Text(Loc.GetF("leash_holder_day", _held.Day), 11.5, F.Gold));
        who.Children.Add(Text(Loc.Get(dnd ? "leash_state_quiet" : _held.Online ? "leash_state_online" : "leash_state_offline"), 10.5, dnd ? F.Lilac : _held.Online ? F.Mint : F.Dim));
        Grid.SetColumn(who, 1); head.Children.Add(who);
        var more = Button("…", "leash-holder-more", F.Muted);
        more.Click += (_, _) =>
        {
            var release = new MenuItem { Header = Loc.GetF("leash_menu_release", _held.Who.Name) };
            release.Click += (_, _) => LeashReleaseWindow.Open(_held.Who.Name, () => _ = ReleaseAsync());
            var menu = new ContextMenu { Items = { release } };
            more.ContextMenu = menu;
            menu.Open(more);
        };
        if (!_readOnly) { Grid.SetColumn(more, 2); head.Children.Add(more); }
        _body.Children.Add(head);
        Figures();
        if (_held.Assignment is { } a)
        {
            string key = a.Status switch { AssignStatus.Done => "leash_task_done", AssignStatus.Missed => "leash_task_missed", _ => "leash_task_open" };
            _body.Children.Add(Text(Loc.GetF(key, AssignText(a)), 11.5, F.Gold, "leash-holder-task"));
        }
        var week = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-week" };
        foreach (var day in _held.Week.TakeLast(7))
        {
            var cell = Text(LeashUiRules.DayLetter(day.Day), 11, day.Mark == WeekMark.Did ? F.Mint : day.Mark == WeekMark.Punished ? F.Red : F.Dim, "leash-week:" + day.Mark);
            cell.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(cell, day.Day);
            week.Children.Add(cell);
        }
        _body.Children.Add(week);
        if (dnd && _held.DndUntil is { } until)
            _body.Children.Add(Text(Loc.GetF("leash_holder_dnd", _held.Who.Name, LeashUiRules.DndUntilText(until)), 11.5, F.Lilac, "leash-holder-dnd"));
        if (_readOnly) { _body.Children.Add(Text(Loc.Get("leash_preview_buttons"), 11, F.Dim)); return; }
        var tools = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var id in new[] { "assign", "reward", "punish", "tug" })
        {
            var action = id;
            var b = Button(Loc.Get("leash_btn_" + id), "leash-btn:" + id + (_sheet == id ? ":open" : ""), id == "reward" ? F.Mint : id == "punish" ? F.Red : F.Gold);
            b.IsEnabled = id == "reward" || !dnd;
            b.Click += async (_, _) => { if (action == "tug") await TugAsync(); else ToggleSheet(action); };
            tools.Children.Add(b);
        }
        _body.Children.Add(tools);
        if (_sheet != null) _body.Children.Add(BuildSheet());
        if (_result is { } r) _body.Children.Add(Text(r.Text, 12, r.Good ? F.Mint : F.Gold, "leash-result"));
        Receipts();
    }

    private void Figures()
    {
        var r = _held.Report;
        var figures = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        void Fig(string value, string label, string tag, string? extra = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(Text(value, 21, F.Text)); sp.Children.Add(Text(Loc.Get(label), 10.5, F.Muted));
            if (extra != null) sp.Children.Add(Text(extra, 10.5, F.Muted));
            figures.Children.Add(new Border { Child = sp, Padding = new Thickness(4, 8), Margin = new Thickness(3, 0), CornerRadius = new CornerRadius(10), Background = Shade, Tag = tag });
        }
        Fig(r == null ? "-" : r.Minutes.ToString(), "leash_fig_minutes", "leash-fig-minutes");
        Fig(r == null ? "-" : r.QuestsDone + "/" + r.QuestsTotal, "leash_fig_quests", "leash-fig-quests", r == null ? null : Loc.GetF("leash_fig_streak", r.Streak));
        if (r is { ChasterLinked: true })
            Fig(r.LockLeftSeconds is int left ? LeashUiRules.LockLeft(left) : "-", "leash_fig_lock", "leash-fig-lock", r.TabSeconds is int tab ? Loc.GetF("leash_fig_tab", LeashUiRules.Tab(tab)) : null);
        else Fig(r == null ? "-" : r.Streak + "d", "leash_fig_streak_label", "leash-fig-streak");
        _body.Children.Add(figures);
    }

    private static string AssignText(Assignment a) => a.Kind switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", a.Size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", a.Size),
        _ => Loc.Get("leash_assign_video_n"),
    };

    internal void ToggleSheet(string id) { _sheet = _sheet == id ? null : id; _videoFor = null; Render(); }

    private Control BuildSheet()
    {
        var rows = new StackPanel { Spacing = 4 };
        string title = _sheet switch
        {
            "assign" => Loc.GetF("leash_sheet_assign", _held.Who.Name),
            "reward" => Loc.Get("leash_sheet_reward"),
            _ => Loc.GetF("leash_sheet_punish", Math.Min(_held.PunishedToday, 3), Loc.Get(LeashUiRules.Key(_held.Intensity))),
        };
        rows.Children.Add(Text(title, 11, F.Muted));
        if (_sheet == "assign")
        {
            if (_held.Assignment is { Status: AssignStatus.Open } a)
                rows.Children.Add(Text(Loc.GetF("leash_sheet_replaces", AssignText(a)), 11, F.Gold));
            foreach (var k in LeashUiRules.AssignOrder)
            {
                var kind = k;
                rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), LeashUiRules.Sizes(k).Select(size =>
                    (k == AssignKind.Video ? Loc.Get("leash_pick") : size.ToString(), $"leash-assign:{k.ToString().ToLowerInvariant()}:{size}",
                    (Func<Task>)(() => kind == AssignKind.Video ? OpenVideo("assign") : SendAssignAsync(kind, size, null))))));
                if (k == AssignKind.Video && _videoFor == "assign") rows.Children.Add(VideoPicker(w => SendAssignAsync(AssignKind.Video, 1, w)));
            }
        }
        else if (_sheet == "reward")
        {
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_sticker"), LeashUiRules.Stickers.Select(id =>
                (Loc.Get("leash_rew_sticker_" + id), "leash-reward:sticker:" + id, (Func<Task>)(() => SendRewardAsync(RewardKind.Sticker, id, null))))));
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_praise"), LeashUiRules.Praise.Select(id =>
                (Loc.Get("leash_praise_" + id), "leash-reward:praise:" + id, (Func<Task>)(() => SendRewardAsync(RewardKind.Praise, id, null))))));
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_pardon"), new[] { (Loc.Get("leash_give"), "leash-reward:pardon", (Func<Task>)(() => SendRewardAsync(RewardKind.Pardon, null, null))) }));
            if (_held.Report?.ChasterLinked == true)
                rows.Children.Add(SizeRow(Loc.Get("leash_rew_credit"), LeashUiRules.CreditSizes.Select(size =>
                    ("-" + LeashUiRules.Clock(size), "leash-reward:credit:" + size, (Func<Task>)(() => SendRewardAsync(RewardKind.Credit, null, size))))));
        }
        else if (_held.PunishedToday >= 3) rows.Children.Add(Text(Loc.Get("leash_sheet_punish_cap"), 12, F.Muted));
        else
        {
            if (_held.Pending.Count > 0) rows.Children.Add(Text(Loc.GetF("leash_sheet_waiting", _held.Pending.Count), 11, F.Gold, "leash-sheet-waiting"));
            foreach (var k in LeashUiRules.VisiblePunishments(_held.Intensity, _held.Report?.ChasterLinked == true))
            {
                var kind = k;
                rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), LeashUiRules.Sizes(k).Select(size =>
                    (k == PunishKind.Video ? Loc.Get("leash_pick") : LeashUiRules.SizeText(k, size), $"leash-punish:{k.ToString().ToLowerInvariant()}:{size}",
                    (Func<Task>)(() => kind == PunishKind.Video ? OpenVideo("punish") : SendPunishAsync(kind, size, null))))));
                if (k == PunishKind.Video && _videoFor == "punish")
                {
                    _videoCap = Math.Min(_videoCap, _held.VideoMax);
                    var cap = new LeashCapSlider(Loc.Get("leash_video_cap_label"), _videoCap, "leash-video-cap") { Ceiling = _held.VideoMax };
                    ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
                    cap.Committed += v => _videoCap = v;
                    rows.Children.Add(cap);
                    rows.Children.Add(VideoPicker(w => SendPunishAsync(PunishKind.Video, _videoCap, w)));
                }
            }
        }
        return new Border { Child = rows, Background = Shade, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 0), Tag = "leash-sheet:" + _sheet };
    }

    private static Control SizeRow(string label, IEnumerable<(string Text, string Tag, Func<Task> Send)> chips)
    {
        var sp = new StackPanel();
        sp.Children.Add(Text(label, 12.5, F.Text));
        var buttons = new WrapPanel();
        foreach (var (text, tag, send) in chips)
        {
            var b = Button(text, tag, F.Gold);
            b.Click += async (_, _) => await send();
            buttons.Children.Add(b);
        }
        sp.Children.Add(buttons);
        return sp;
    }

    private Task OpenVideo(string forWhat) { _videoFor = _videoFor == forWhat ? null : forWhat; Render(); return Task.CompletedTask; }

    private Control VideoPicker(Func<LeashWatch, Task> send)
    {
        var sp = new StackPanel { Spacing = 3, Tag = "leash-video-picker" };
        foreach (var (id, title) in FriendsInviteCodes.CatalogueWatches().Take(5))
        {
            var b = Button(title, "leash-catalogue:" + id, F.Text);
            b.Click += async (_, _) => await send(new LeashWatch("catalogue", id, title));
            sp.Children.Add(b);
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox { FontFamily = F.Mono, FontSize = 12.5, Tag = "leash-ht-box" };
        ToolTip.SetTip(box, Loc.Get("leash_video_ht_hint"));
        var go = Button(Loc.Get("leash_send"), "leash-video-send", F.Gold);
        go.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id != box.Text) { box.Text = id; box.CaretIndex = id.Length; }
            go.IsEnabled = id.Length > 0;
        };
        go.Click += async (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length > 0) await send(new LeashWatch("ht", id, null));
        };
        row.Children.Add(box); Grid.SetColumn(go, 1); row.Children.Add(go);
        sp.Children.Add(row);
        return sp;
    }

    private void Receipts()
    {
        if (!ReceiptsOn) return;
        IReadOnlyList<LeashSentItem> items;
        try { items = _service()?.SentTo(_held.Who.Id) ?? Array.Empty<LeashSentItem>(); } catch { return; }
        if (items.Count == 0) return;
        var sp = new StackPanel { Margin = new Thickness(2, 10, 0, 0), Tag = "leash-sent" };
        sp.Children.Add(Text(Loc.Get("leash_sent_title"), 11, F.Muted));
        var now = DateTimeOffset.UtcNow;
        foreach (var i in items.Take(LeashUiRules.SentRows))
        {
            var accent = i.Step switch { LeashStep.Sent => F.Dim, LeashStep.Arrived or LeashStep.Skipped => F.Lilac, LeashStep.Seen => F.Gold, LeashStep.Done or LeashStep.Accepted => F.Mint, _ => F.Red };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Tag = $"leash-sent-row:{i.Kind.ToString().ToLowerInvariant()}:{i.Step.ToString().ToLowerInvariant()}" };
            ToolTip.SetTip(row, LeashUiRules.Timeline(i, now, Loc.Get));
            var what = Text(LeashUiRules.ItemText(i, Loc.Get), 11.5, F.Text);
            what.MaxLines = 1; what.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(what);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            int n = 0;
            foreach (var lit in LeashUiRules.Lit(i))
                right.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), BorderBrush = lit ? accent : F.Line2, BorderThickness = new Thickness(1), Background = lit ? accent : Brushes.Transparent, Tag = $"leash-step-dot:{n++}:{(lit ? "on" : "off")}", VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(Text(Loc.Get(LeashUiRules.StepKey(i.Step)), 10.5, accent, "leash-step-word"));
            right.Children.Add(Text(LeashUiRules.StepTime(i.StepAt, now), 10, F.Dim));
            Grid.SetColumn(right, 1); row.Children.Add(right); sp.Children.Add(row);
        }
        _body.Children.Add(sp);
    }

    internal async Task<LeashSendStatus> SendPunishAsync(PunishKind kind, int size, LeashWatch? watch)
    {
        var r = await Guarded(s => s.PunishAsync(_held.Who.Id, kind, size, watch));
        CloseGoodSheet(r, true);
        Word(r, Loc.GetF(r.Status == LeashSendStatus.Queued ? "leash_done_punish_queued" : "leash_done_punish", _held.Who.Name));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendAssignAsync(AssignKind kind, int size, LeashWatch? watch)
    {
        var r = await Guarded(s => s.AssignAsync(_held.Who.Id, kind, size, watch));
        CloseGoodSheet(r, true); Word(r, Honest("leash_done_assign")); return r.Status;
    }

    internal async Task<LeashSendStatus> SendRewardAsync(RewardKind kind, string? token, int? size)
    {
        var r = await Guarded(s => s.RewardAsync(_held.Who.Id, kind, token, size));
        CloseGoodSheet(r, false); Word(r, Honest("leash_done_reward")); return r.Status;
    }

    internal async Task<LeashSendStatus> TugAsync()
    {
        var r = !TugThrottle.TryTug(_held.Who.Id, DateTime.UtcNow) ? new LeashSendResult(LeashSendStatus.TooFast)
            : await Guarded(s => s.TugAsync(_held.Who.Id));
        Word(r, Honest("leash_done_tug")); return r.Status;
    }

    internal async Task<bool> ReleaseAsync()
    {
        bool ok = false;
        try { if (_service() is { } s) ok = await s.ReleaseAsync(_held.Who.Id); } catch { }
        // WPF reports whether the server took the release.
        try { App.Notifications?.Show(Loc.GetF(ok ? "leash_release_done" : "leash_release_failed", _held.Who.Name), ok ? Helpers.NotificationType.Info : Helpers.NotificationType.Warning); } catch { }
        return ok;
    }

    private void CloseGoodSheet(LeashSendResult r, bool video)
    {
        if (!LeashUiRules.IsGood(r.Status)) return;
        _sheet = null; if (video) _videoFor = null;
    }
    private string Honest(string key) => ReceiptsOn ? Loc.Get("leash_res_sent") : Loc.GetF(key, _held.Who.Name);
    private async Task<LeashSendResult> Guarded(Func<ILeashService, Task<LeashSendResult>> call)
    {
        try { return _service() is { } s ? await call(s) : new LeashSendResult(LeashSendStatus.Off); }
        catch { return new LeashSendResult(LeashSendStatus.Failed); }
    }
    private void Word(LeashSendResult r, string goodText)
    {
        bool good = LeashUiRules.IsGood(r.Status);
        _result = (good ? goodText : r.Status == LeashSendStatus.Dnd && r.DndUntil is { } u
            ? Loc.GetF("leash_res_dnd_until", _held.Who.Name, LeashUiRules.DndUntilText(u)) : Loc.Get(LeashUiRules.ResultKey(r.Status)), good);
        _resultTimer?.Stop();
        _resultUntil = DateTimeOffset.UtcNow.AddSeconds(3);
        _resultTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _resultTimer.Tick += (_, _) => { _resultTimer?.Stop(); _result = null; Render(); };
        _resultTimer.Start(); Render();
    }

    private static TextBlock Text(string text, double size, IBrush brush, string? tag = null)
    {
        var t = F.Label(text, size, brush); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; t.Tag = tag; return t;
    }
    private static Button Button(string text, string tag, IBrush accent)
    {
        var b = F.Pill(text, Shade, accent, tag, F.Line2); b.Margin = new Thickness(2); return b;
    }
}
