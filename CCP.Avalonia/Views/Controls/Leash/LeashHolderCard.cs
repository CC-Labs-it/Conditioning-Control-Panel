// WPF LeashHolderCard.cs:83, 339, 423, 432, 542-769: holder report, sheets and sends.
// Flat cards reuse FriendsDrawer chrome. Offer, snap, explainers and FX remain gaps.
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
    internal static string TaskText(Assignment a) => a.Kind switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", a.Size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", a.Size),
        _ => Loc.Get("leash_assign_video_n"),
    };
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
        _body.Children.Add(Figures());
        if (h.Assignment is { } a) _body.Children.Add(LeashCardUi.Text(Loc.GetF(a.Status switch
        { AssignStatus.Done => "leash_task_done", AssignStatus.Missed => "leash_task_missed", _ => "leash_task_open" }, LeashCardUi.TaskText(a)), "leash-holder-task"));
        var week = new WrapPanel { Tag = "leash-week" };
        foreach (var day in h.Week.TakeLast(7)) week.Children.Add(LeashCardUi.Text(R.DayLetter(day.Day), "leash-week:" + day.Mark,
            day.Mark switch { WeekMark.Did => F.Mint, WeekMark.Punished => F.Red, WeekMark.Today => F.Gold, _ => F.Dim }));
        _body.Children.Add(week);
        if (dnd && h.DndUntil is { } quietUntil)
            _body.Children.Add(LeashCardUi.Text(Loc.GetF("leash_holder_dnd", h.Who.Name, R.DndUntilText(quietUntil)), "leash-holder-dnd", F.Lilac));
        if (_readOnly) { _body.Children.Add(LeashCardUi.Text(Loc.Get("leash_preview_buttons"), ink: F.Dim)); return; }
        var buttons = new WrapPanel();
        foreach (var id in new[] { "assign", "reward", "punish", "tug" })
        {
            var button = LeashCardUi.Action(Loc.Get("leash_btn_" + id), "leash-btn:" + id, async () =>
            {
                if (id == "tug")
                {
                    if (!TugThrottle.TryTug(h.Who.Id, DateTime.UtcNow)) { Word(new(LeashSendStatus.TooFast), ""); return; }
                    await Send(s => s.TugAsync(h.Who.Id), Honest("leash_done_tug"), close: false);
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
                        if (kind == PunishKind.Video) panel.Children.Add(VideoPicker(w => Send(s => s.PunishAsync(h.Who.Id, kind, _videoCap, w), Punished), true));
                        else panel.Children.Add(LeashCardUi.Choices(R.Sizes(kind).Select(size =>
                            (R.SizeText(kind, size), $"leash-punish:{kind.ToString().ToLowerInvariant()}:{size}", (Func<Task>)(() => Send(s => s.PunishAsync(h.Who.Id, kind, size), Punished))))));
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
                    if (kind == AssignKind.Video) panel.Children.Add(VideoPicker(w => Send(s => s.AssignAsync(h.Who.Id, kind, 1, w), Honest("leash_done_assign")), false));
                    else panel.Children.Add(LeashCardUi.Choices(R.Sizes(kind).Select(size =>
                        (size.ToString(), $"leash-assign:{kind.ToString().ToLowerInvariant()}:{size}", (Func<Task>)(() => Send(s => s.AssignAsync(h.Who.Id, kind, size), Honest("leash_done_assign")))))));
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
        // WPF LeashHolderCard.cs:310: letting go asks first; it ends the leash for both and sets a 24 h cooldown.
        _body.Children.Add(LeashCardUi.Action(Loc.GetF("leash_menu_release", h.Who.Name), "leash-release", () =>
        {
            LeashCutConfirmWindow.Confirm(Loc.GetF("leash_release_confirm_title", h.Who.Name), Loc.GetF("leash_release_confirm_body", h.Who.Name),
                Loc.Get("leash_release_confirm_yes"), Loc.Get("leash_cut_confirm_no"), "leash-release-confirm", () => _ = Release(h.Who));
            return Task.CompletedTask;
        }, IconKind.Link));
        if (LeashSentList.For(_service(), h.Who.Id) is { } receipts) _body.Children.Add(receipts);
        if (_result != null) _body.Children.Add(LeashCardUi.Text(_result, "leash-holder-result", F.Gold));
    }
    // WPF LeashHolderCard.cs:339-397: three figures, with goal-relative minutes and unknowns kept unknown.
    private Control Figures()
    {
        var r = _held.Report;
        var row = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Tag = "leash-figures", Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(Fig(LeashHoldRing.Ring(R.RingFraction(r?.Minutes ?? 0, R.MinutesGoal(_held.Assignment)),
            r?.Minutes.ToString() ?? "-", ((ISolidColorBrush)F.Pink).Color, 48), "leash_fig_minutes", null, "leash-fig-minutes"));
        row.Children.Add(Fig(Figure(r == null ? "-" : $"{r.QuestsDone}/{r.QuestsTotal}", F.Text), "leash_fig_quests",
            r == null ? null : Loc.GetF("leash_fig_streak", r.Streak), "leash-fig-quests"));
        if (r?.ChasterLinked == true)
            row.Children.Add(Fig(Figure(r.LockLeftSeconds is int left ? R.LockLeft(left) : "-", F.Gold, 17), "leash_fig_lock",
                r.TabSeconds is int tab ? Loc.GetF("leash_fig_tab", R.Tab(tab)) : null, "leash-fig-lock",
                r.TabSeconds > 0 ? F.Red : F.Mint));
        else row.Children.Add(Fig(Figure(r == null ? "-" : r.Streak + "d", F.Lilac), "leash_fig_streak_label", null, "leash-fig-streak"));
        return row;
    }
    private static Control Figure(string text, IBrush ink, double size = 21) => new Grid
    {
        Height = 48, Children = { new TextBlock { Text = text, Foreground = ink, FontSize = size, FontFamily = F.Mono,
            FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
    };
    private static Border Fig(Control visual, string key, string? extra, string tag, IBrush? extraInk = null)
    {
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(visual);
        body.Children.Add(LeashCardUi.Text(Loc.Get(key), ink: F.Muted));
        if (extra != null) body.Children.Add(LeashCardUi.Text(extra, ink: extraInk ?? F.Muted));
        return new Border { Child = body, Tag = tag, Background = F.Foot, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 8, 4, 7), Margin = new Thickness(3, 0) };
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
        // Text property, not TextChanged (raised a frame later): same as FriendsDrawer.Pickers.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id != box.Text) { box.Text = id; box.CaretIndex = id.Length; }
            go.IsEnabled = id.Length > 0;
        };
        panel.Children.Add(box);
        panel.Children.Add(go);
        return panel;
    }
    private async Task Release(LeashPerson who)
    {
        var ok = false;
        try { ok = _service() is { } s && await s.ReleaseAsync(who.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] release failed: {E}", ex.Message); }
        App.Notifications?.Show(Loc.GetF(ok ? "leash_release_done" : "leash_release_failed", who.Name),
            ok ? Helpers.NotificationType.Info : Helpers.NotificationType.Warning);
    }
    private Task Reward(RewardKind kind, string? token = null, int? size = null) =>
        Send(s => s.RewardAsync(_held.Who.Id, kind, token, size), Honest("leash_done_reward"));
    // WPF LeashHolderCard.cs:722/744: a punishment always names its friend; other sends say "Sent." once receipts exist.
    private string Punished(LeashSendResult r) => Loc.GetF(r.Status == LeashSendStatus.Queued ? "leash_done_punish_queued" : "leash_done_punish", _held.Who.Name);
    private Func<LeashSendResult, string> Honest(string oldKey) => _ =>
        _service()?.ReceiptsSupported == true ? Loc.Get("leash_res_sent") : Loc.GetF(oldKey, _held.Who.Name);
    private async Task Send(Func<ILeashService, Task<LeashSendResult>> call, Func<LeashSendResult, string> good, bool close = true)
    {
        LeashSendResult result;
        try { result = _service() is { } s ? await call(s) : new(LeashSendStatus.Off); }
        catch { result = new(LeashSendStatus.Failed); }
        if (close && R.IsGood(result.Status)) _sheet = null;
        Word(result, R.IsGood(result.Status) ? good(result) : "");
    }
    private void Word(LeashSendResult result, string goodText)
    {
        _result = R.IsGood(result.Status) ? goodText
            : result.Status == LeashSendStatus.Dnd && result.DndUntil is { } until
                ? Loc.GetF("leash_res_dnd_until", _held.Who.Name, R.DndUntilText(until)) : Loc.Get(R.ResultKey(result.Status));
        _resultTimer.Stop();
        _resultTimer.Start();
        Render();
    }
}
