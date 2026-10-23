// Adapted from WPF LeashSelfCard, LeashHolderCard and LeashDrawerSection at 888cdbffe.
// ponytail: flat cards reuse FriendsDrawer chrome; offer/ask/snap, explainers and FX remain separate gaps.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using R = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal static class LeashCardUi
{
    internal static TextBlock Text(string text, string? tag = null, IBrush? ink = null) => new()
    {
        Text = text, Tag = tag, Foreground = ink ?? F.Text, FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4),
    };
    internal static Button Action(string text, string tag, Func<Task> run, IconKind? icon = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (icon is { } k) content.Children.Add(new IconGlyph { Kind = k, Size = 15 });
        content.Children.Add(Text(text));
        var b = F.Pill(content, F.Raised, F.Text, tag, F.Line2);
        b.Margin = new Thickness(2, 3);
        b.Click += async (_, _) =>
        {
            if (!b.IsEnabled) return;
            b.IsEnabled = false;
            try { await run(); }
            catch (Exception ex) { Serilog.Log.Debug("Leash action failed: {E}", ex.Message); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }
    internal static WrapPanel Choices(IEnumerable<(string Text, string Tag, Func<Task> Run)> choices)
    {
        var row = new WrapPanel();
        foreach (var c in choices) row.Children.Add(Action(c.Text, c.Tag, c.Run));
        return row;
    }
    internal static void Frame(Border card, StackPanel body, string tag)
    {
        card.Tag = tag;
        card.Background = F.Foot;
        card.BorderBrush = F.Line2;
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(14);
        card.Padding = new Thickness(12);
        card.Margin = new Thickness(0, 4, 0, 6);
        card.Child = body;
    }
    internal static string TaskText(Assignment a) => Loc.GetF(a.Kind switch
    {
        AssignKind.Minutes => "leash_assign_minutes_n", AssignKind.Quests => "leash_assign_quests_n", _ => "leash_assign_video_n",
    }, a.Size);
}

/// <summary>The leashed player's controls. Cut is first, immediate, and never asks for confirmation.</summary>
public sealed class LeashSelfCard : Border
{
    private MyLeash _me;
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _body = new();
    private bool _preview;
    internal LeashSelfCard() : this(new MyLeash(new("holder", "Friend", null), LeashIntensity.Standard,
        DateTimeOffset.UtcNow, 1, null, LeashRemoteMode.Ask, Array.Empty<Punishment>(), null, 1, Array.Empty<Sticker>()), () => null) { }
    public LeashSelfCard(MyLeash me, Func<ILeashService?> service)
    {
        (_me, _service) = (me, service);
        LeashCardUi.Frame(this, _body, "leash-self-card");
        Render();
    }
    internal void Update(MyLeash me) { _me = me; Render(); }
    private void Render()
    {
        _body.Children.Clear();
        _body.Children.Add(LeashCardUi.Text(_me.Holder.Name + " " + Loc.Get("leash_self_holds"), "leash-self-title", F.Gold));
        _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_day", _me.Day)));
        _body.Children.Add(LeashCardUi.Action(Loc.Get("leash_cut"), "leash-cut", async () =>
        {
            LeashTaskHost.EndTask("cut");
            App.Notifications.Show(Loc.Get("leash_cut_done"), Helpers.NotificationType.Success);
            if (_service() is { } s) await s.CutAsync();
        }, IconKind.Cut));
        var gates = _me.Pending.Count(p => p.Kind != PunishKind.Chaster);
        if (gates > 0) _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_pending", gates)));
        if (_me.Assignment is { } a)
        {
            _body.Children.Add(LeashCardUi.Text(Loc.GetF(a.Status switch
            {
                AssignStatus.Done => "leash_self_task_done", AssignStatus.Missed => "leash_self_task_missed", _ => "leash_self_task_open",
            }, LeashCardUi.TaskText(a), _me.Holder.Name), "leash-self-task"));
            if (LeashAssignRule.OfferWatch(a))
            {
                var running = LeashTaskHost.Runner?.RunningAid == a.Aid;
                var watch = LeashCardUi.Action(Loc.Get(running ? "leash_self_watching" : "leash_self_watch"), "leash-self-watch", () =>
                {
                    if (LeashTaskHost.Runner?.StartAssignmentWatch(a) != true)
                        App.Notifications.Show(Loc.Get(LeashTaskHost.Runner?.IsRunning == true ? "leash_self_watch_busy" : "leash_self_watch_cannot"), Helpers.NotificationType.Warning);
                    Render();
                    return Task.CompletedTask;
                }, IconKind.Play);
                watch.IsEnabled = !running;
                _body.Children.Add(watch);
            }
        }
        if (_me.Pardons > 0) _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_pardons", _me.Pardons)));
        if (gates == 0 && _me.Assignment == null && _me.Pardons == 0) _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_self_clear")));
        _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_self_level") + ": " + Loc.Get(R.Key(_me.Intensity))));
        _body.Children.Add(LeashCardUi.Choices(Enum.GetValues<LeashIntensity>().Select(i =>
            (Loc.Get(R.Key(i)), "leash-level:" + i.ToString().ToLowerInvariant(), (Func<Task>)(async () =>
            {
                if (i == _me.Intensity) return;
                _me = _me with { Intensity = i };
                Render();
                if (_service() is { } s) await s.SetIntensityAsync(i);
            })))));
        var dnd = R.DndOn(_me.DndUntil, DateTimeOffset.UtcNow);
        _body.Children.Add(LeashCardUi.Text(dnd && _me.DndUntil is { } until
            ? Loc.GetF("leash_self_quiet_until", R.DndUntilText(until)) : Loc.Get("leash_self_quiet"), "leash-self-quiet"));
        _body.Children.Add(LeashCardUi.Choices(new[] { "off", "1h", "4h", "today" }.Select((id, i) =>
            (Loc.Get("leash_dnd_" + id), "leash-dnd:" + id, (Func<Task>)(async () =>
            { if (_service() is { } s) await s.SetDndAsync((LeashDnd)i); })))));
        _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_video_max")));
        var cap = new NumericUpDown { Minimum = 1, Maximum = 90, Increment = 1, Value = _me.VideoMax, FormatString = "0", Tag = "leash-video-max" };
        // ponytail: the existing numeric control commits whole minutes on change instead of WPF's drag-release slider.
        cap.ValueChanged += async (_, _) =>
        {
            if (cap.Value is not { } value || value != decimal.Truncate(value)) return;
            var minutes = (int)Math.Clamp(value, 1, 90);
            if (minutes == _me.VideoMax) return;
            _me = _me with { VideoMax = minutes };
            try { if (_service() is { } s) await s.SetVideoMaxAsync(minutes); } catch { }
        };
        ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
        _body.Children.Add(cap);
        _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey)), "leash-self-hold"));
        if (_me.Stickers.Count > 0)
        {
            _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_shelf", _me.Stickers.Count)));
            var shelf = new WrapPanel { Tag = "leash-shelf" };
            foreach (var sticker in _me.Stickers.Reverse().Take(14))
            {
                var item = LeashCardUi.Text(Loc.Get("leash_rew_sticker_" + sticker.Id), ink: F.Gold);
                ToolTip.SetTip(item, Loc.GetF("leash_sticker_from", sticker.From.Name));
                shelf.Children.Add(item);
            }
            _body.Children.Add(shelf);
        }
        _body.Children.Add(LeashCardUi.Action(Loc.GetF(_preview ? "leash_self_preview_hide" : "leash_self_preview", _me.Holder.Name), "leash-preview-toggle", () =>
        { _preview = !_preview; Render(); return Task.CompletedTask; }, IconKind.Eye));
        if (_preview)
        {
            var report = (_service() as LeashService)?.LastReport;
            _body.Children.Add(new LeashHolderCard(new HeldLeash(new LeashPerson("me", Loc.Get("leash_you"), null), true,
                _me.Intensity, _me.Since, _me.Day, _me.DndUntil, report, Array.Empty<WeekDay>(), _me.Pending, _me.Assignment, 0), () => null, true));
            _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_self_preview_note", _me.Holder.Name)));
        }
    }
}

/// <summary>One held friend. Open sheets and feedback survive polls; receipts repaint in place.</summary>
public sealed class LeashHolderCard : Border
{
    private HeldLeash _held;
    private readonly Func<ILeashService?> _service;
    private readonly bool _readOnly;
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private static readonly LeashTugThrottle TugThrottle = new();
    private string? _sheet, _result;
    internal LeashHolderCard() : this(new HeldLeash(new("friend", "Friend", null), true, LeashIntensity.Standard,
        DateTimeOffset.UtcNow, 1, null, new DayReport("20261023", 30, 2, 3, 4, false, null, null, false, DateTimeOffset.UtcNow),
        Array.Empty<WeekDay>(), Array.Empty<Punishment>(), null, 0), () => null) { }
    public LeashHolderCard(HeldLeash held, Func<ILeashService?> service, bool readOnly = false)
    {
        (_held, _service, _readOnly) = (held, service, readOnly);
        LeashCardUi.Frame(this, _body, readOnly ? "leash-preview" : "leash-holder-card:" + held.Who.Id);
        _resultTimer.Tick += (_, _) => { _resultTimer.Stop(); _result = null; Render(); };
        DetachedFromVisualTree += (_, _) => _resultTimer.Stop();
        Render();
    }
    internal void Update(HeldLeash held) { _held = held; Render(); }
    internal void Fold() { _sheet = null; Render(); }
    private void Render()
    {
        _body.Children.Clear();
        var h = _held;
        var dnd = R.DndOn(h.DndUntil, DateTimeOffset.UtcNow);
        _body.Children.Add(LeashCardUi.Text(h.Who.Name, ink: F.Gold));
        _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_holder_day", h.Day)));
        _body.Children.Add(LeashCardUi.Text(Loc.Get(dnd ? "leash_state_quiet" : h.Online ? "leash_state_online" : "leash_state_offline"), "leash-holder-state"));
        var r = h.Report;
        _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_fig_minutes") + ": " + (r?.Minutes.ToString() ?? "-"), "leash-fig-minutes"));
        _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_fig_quests") + ": " + (r == null ? "-" : $"{r.QuestsDone}/{r.QuestsTotal}"), "leash-fig-quests"));
        _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_fig_streak", r?.Streak ?? 0), "leash-fig-streak"));
        if (r?.ChasterLinked == true)
        {
            _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_fig_lock") + ": " + (r.LockLeftSeconds is int left ? R.LockLeft(left) : "-"), "leash-fig-lock"));
            if (r.TabSeconds is int tab) _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_fig_tab", R.Tab(tab))));
        }
        if (h.Assignment is { } a) _body.Children.Add(LeashCardUi.Text(Loc.GetF(a.Status switch
        { AssignStatus.Done => "leash_task_done", AssignStatus.Missed => "leash_task_missed", _ => "leash_task_open" }, LeashCardUi.TaskText(a)), "leash-holder-task"));
        var week = new WrapPanel { Tag = "leash-week" };
        foreach (var day in h.Week.TakeLast(7)) week.Children.Add(LeashCardUi.Text(R.DayLetter(day.Day), "leash-week:" + day.Mark,
            day.Mark switch { WeekMark.Did => F.Mint, WeekMark.Punished => F.Red, WeekMark.Today => F.Gold, _ => F.Dim }));
        _body.Children.Add(week);
        if (_readOnly) return;
        var buttons = new WrapPanel();
        foreach (var id in new[] { "assign", "reward", "punish", "tug" })
        {
            var button = LeashCardUi.Action(Loc.Get("leash_btn_" + id), "leash-btn:" + id, async () =>
            {
                if (id == "tug")
                {
                    if (!TugThrottle.TryTug(h.Who.Id, DateTime.UtcNow)) { Word(new(LeashSendStatus.TooFast), "leash_done_tug"); return; }
                    await Send(s => s.TugAsync(h.Who.Id), "leash_done_tug", close: false);
                }
                else { _sheet = _sheet == id ? null : id; Render(); }
            });
            button.IsEnabled = id == "reward" || !dnd;
            buttons.Children.Add(button);
        }
        _body.Children.Add(buttons);
        if (_sheet is { } sheet)
        {
            var panel = new StackPanel { Tag = "leash-sheet:" + sheet };
            if (sheet == "punish")
            {
                panel.Children.Add(LeashCardUi.Text(Loc.GetF("leash_sheet_punish", Math.Min(h.PunishedToday, 3), Loc.Get(R.Key(h.Intensity)))));
                if (h.PunishedToday >= 3) panel.Children.Add(LeashCardUi.Text(Loc.Get("leash_sheet_punish_cap")));
                else
                {
                    if (h.Pending.Count > 0) panel.Children.Add(LeashCardUi.Text(Loc.GetF("leash_sheet_waiting", h.Pending.Count)));
                    foreach (var kind in R.VisiblePunishments(h.Intensity, r?.ChasterLinked == true))
                    {
                        panel.Children.Add(LeashCardUi.Text(Loc.Get(R.Key(kind))));
                        if (kind == PunishKind.Video) panel.Children.Add(VideoPicker(w => Send(s => s.PunishAsync(h.Who.Id, kind, _videoCap, w), "leash_done_punish"), true));
                        else panel.Children.Add(LeashCardUi.Choices(R.Sizes(kind).Select(size =>
                            (R.SizeText(kind, size), $"leash-punish:{kind.ToString().ToLowerInvariant()}:{size}", (Func<Task>)(() => Send(s => s.PunishAsync(h.Who.Id, kind, size), "leash_done_punish"))))));
                    }
                }
            }
            else if (sheet == "assign")
            {
                panel.Children.Add(LeashCardUi.Text(Loc.GetF("leash_sheet_assign", h.Who.Name)));
                if (h.Assignment is { Status: AssignStatus.Open } current) panel.Children.Add(LeashCardUi.Text(Loc.GetF("leash_sheet_replaces", LeashCardUi.TaskText(current))));
                foreach (var kind in R.AssignOrder)
                {
                    panel.Children.Add(LeashCardUi.Text(Loc.Get(R.Key(kind))));
                    if (kind == AssignKind.Video) panel.Children.Add(VideoPicker(w => Send(s => s.AssignAsync(h.Who.Id, kind, 1, w), "leash_done_assign"), false));
                    else panel.Children.Add(LeashCardUi.Choices(R.Sizes(kind).Select(size =>
                        (size.ToString(), $"leash-assign:{kind.ToString().ToLowerInvariant()}:{size}", (Func<Task>)(() => Send(s => s.AssignAsync(h.Who.Id, kind, size), "leash_done_assign"))))));
                }
            }
            else
            {
                panel.Children.Add(LeashCardUi.Choices(R.Stickers.Select(id =>
                    (Loc.Get("leash_rew_sticker_" + id), "leash-reward:sticker:" + id, (Func<Task>)(() => Reward(RewardKind.Sticker, id))))));
                panel.Children.Add(LeashCardUi.Choices(R.Praise.Select(id =>
                    (Loc.Get("leash_praise_" + id), "leash-reward:praise:" + id, (Func<Task>)(() => Reward(RewardKind.Praise, id))))));
                panel.Children.Add(LeashCardUi.Action(Loc.Get("leash_rew_pardon"), "leash-reward:pardon", () => Reward(RewardKind.Pardon)));
                if (r?.ChasterLinked == true) panel.Children.Add(LeashCardUi.Choices(R.CreditSizes.Select(size =>
                    ("-" + R.Span(size), "leash-reward:credit:" + size, (Func<Task>)(() => Reward(RewardKind.Credit, size: size))))));
            }
            _body.Children.Add(panel);
        }
        _body.Children.Add(LeashCardUi.Action(Loc.GetF("leash_menu_release", h.Who.Name), "leash-release", async () =>
        {
            var ok = _service() is { } s && await s.ReleaseAsync(h.Who.Id);
            App.Notifications.Show(Loc.GetF(ok ? "leash_release_done" : "leash_release_failed", h.Who.Name), Helpers.NotificationType.Info);
        }, IconKind.Link));
        if (LeashSentList.For(_service(), h.Who.Id) is { } receipts) _body.Children.Add(receipts);
        if (_result != null) _body.Children.Add(LeashCardUi.Text(_result, "leash-holder-result", F.Gold));
    }
    private int _videoCap = LeashVideoCap.Default;
    private Control VideoPicker(Func<LeashWatch, Task> send, bool punish)
    {
        var panel = new StackPanel { Tag = "leash-video-picker" };
        if (punish)
        {
            _videoCap = Math.Min(_videoCap, _held.VideoMax);
            var cap = new NumericUpDown { Minimum = 1, Maximum = _held.VideoMax, Value = _videoCap, FormatString = "0", Tag = "leash-video-cap" };
            cap.ValueChanged += (_, _) => _videoCap = (int)Math.Clamp(cap.Value ?? 1, 1, _held.VideoMax);
            panel.Children.Add(LeashCardUi.Text(Loc.Get("leash_video_cap_label")));
            panel.Children.Add(cap);
        }
        foreach (var (id, title) in FriendsInviteCodes.CatalogueWatches().Take(5))
            panel.Children.Add(LeashCardUi.Action(title, "leash-video:" + id, () => send(new("catalogue", id, title))));
        var box = new TextBox { Tag = "leash-ht-box", PlaceholderText = Loc.Get("leash_video_ht_hint") };
        var go = LeashCardUi.Action(Loc.Get("leash_send"), "leash-video-send", () => send(new("ht", FriendsDrawerRules.NormaliseHtId(box.Text), null)));
        go.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id != box.Text) { box.Text = id; box.CaretIndex = id.Length; }
            go.IsEnabled = id.Length > 0;
        };
        panel.Children.Add(box);
        panel.Children.Add(go);
        return panel;
    }
    private Task Reward(RewardKind kind, string? token = null, int? size = null) =>
        Send(s => s.RewardAsync(_held.Who.Id, kind, token, size), "leash_done_reward");
    private async Task Send(Func<ILeashService, Task<LeashSendResult>> call, string oldKey, bool close = true)
    {
        LeashSendResult result;
        try { result = _service() is { } s ? await call(s) : new(LeashSendStatus.Off); }
        catch { result = new(LeashSendStatus.Failed); }
        if (close && R.IsGood(result.Status)) _sheet = null;
        Word(result, oldKey);
    }
    private void Word(LeashSendResult result, string oldKey)
    {
        _result = R.IsGood(result.Status)
            ? _service()?.ReceiptsSupported == true ? Loc.Get("leash_res_sent") : Loc.GetF(oldKey, _held.Who.Name)
            : result.Status == LeashSendStatus.Dnd && result.DndUntil is { } until
                ? Loc.GetF("leash_res_dnd_until", _held.Who.Name, R.DndUntilText(until)) : Loc.Get(R.ResultKey(result.Status));
        _resultTimer.Stop();
        _resultTimer.Start();
        Render();
    }
}

/// <summary>Live self card then held friends, shared by the page and friends drawer.</summary>
public sealed class LeashDrawerSection : StackPanel
{
    private readonly Func<ILeashService?> _resolve;
    private ILeashService? _service;
    private LeashSelfCard? _self;
    private readonly Dictionary<string, LeashHolderCard> _holders = new();
    public event Action? Changed;
    public LeashDrawerSection() : this(() => LeashHead.Service) { }
    internal LeashDrawerSection(Func<ILeashService?> resolve)
    {
        _resolve = resolve;
        Tag = "leash-section";
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Unsubscribe();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsEffectivelyVisible) NoteShown(); };
    }
    internal void Refresh()
    {
        var next = _resolve();
        if (!ReferenceEquals(next, _service))
        {
            Unsubscribe();
            _service = next;
            if (next != null) { next.SnapshotChanged += OnSnapshot; next.ReceiptsChanged += Render; }
        }
        Render();
    }
    internal void Deactivate() { FoldForPanic(); Unsubscribe(); }
    private void Unsubscribe()
    {
        if (_service != null) { _service.SnapshotChanged -= OnSnapshot; _service.ReceiptsChanged -= Render; }
        _service = null;
    }
    private void OnSnapshot(LeashSnapshot _) => Render();
    private void Render()
    {
        var cards = new List<Control>();
        var snapshot = _service?.Available == true ? _service.Snapshot : LeashSnapshot.Empty;
        if (snapshot.Me is { } me)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _service);
            else _self.Update(me);
            cards.Add(_self);
        }
        else _self = null;
        foreach (var held in snapshot.Holding)
        {
            if (!_holders.TryGetValue(held.Who.Id, out var card))
                _holders[held.Who.Id] = card = new LeashHolderCard(held, () => _service);
            else card.Update(held);
            cards.Add(card);
        }
        foreach (var gone in _holders.Keys.Except(snapshot.Holding.Select(h => h.Who.Id)).ToArray()) _holders.Remove(gone);
        foreach (var gone in Children.Where(c => !cards.Contains(c)).ToArray()) Children.Remove(gone);
        for (var i = 0; i < cards.Count; i++)
            if (i >= Children.Count || !ReferenceEquals(Children[i], cards[i]))
            {
                Children.Remove(cards[i]);
                Children.Insert(i, cards[i]);
            }
        IsVisible = Children.Count > 0;
        Changed?.Invoke();
        NoteShown();
    }
    internal void NoteShown()
    {
        if (TopLevel.GetTopLevel(this) != null && IsEffectivelyVisible)
            _service?.NoteShown(_service.Snapshot.Me?.Assignment?.Aid);
    }
    internal void FoldForPanic() { foreach (var card in _holders.Values) card.Fold(); }
}
