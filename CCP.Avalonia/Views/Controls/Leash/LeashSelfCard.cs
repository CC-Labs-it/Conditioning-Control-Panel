// Adapted from WPF Controls/Leash/LeashSelfCard.cs at 73b1ccf0d.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using Rules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>The player's own leash: immediate cut first, waiting tasks, then player-owned limits.</summary>
public sealed class LeashSelfCard : Border
{
    private MyLeash _me;
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _body = new() { Spacing = 8 };

    public LeashSelfCard(MyLeash me, Func<ILeashService?> service)
    {
        (_me, _service) = (me, service);
        Tag = "leash-self-card";
        Background = F.Foot;
        BorderBrush = F.Pink;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Child = _body;
        Render();
    }

    /// <summary>--render-view sample (internal: no production caller can ship it).</summary>
    internal LeashSelfCard() : this(Sample(DateTimeOffset.UtcNow), () => null) { }

    private static MyLeash Sample(DateTimeOffset now)
    {
        var vex = new LeashPerson("vex", "Vex", null);
        return new MyLeash(vex, LeashIntensity.Standard, now.AddDays(-3), 4, null, LeashRemoteMode.Ask,
            new[] { new Punishment("p1", PunishKind.Lines, 5, null, vex, now.AddMinutes(-9), now.AddHours(20)) },
            null, 1, new[] { new Sticker("good-girl", vex, now.AddHours(-2)) });
    }

    public void Update(MyLeash me) { _me = me; Render(); }

    private void Line(string text, string tag)
    {
        var line = F.Label(text, 12, F.Text);
        line.TextWrapping = TextWrapping.Wrap;
        line.Tag = tag;
        _body.Children.Add(line);
    }

    private Button Action(string key, string tag, Action act)
    {
        var b = F.Pill(Loc.Get(key), F.Raised, F.Text, tag, F.Line2);
        b.Click += (_, _) => act();
        return b;
    }

    private void Render()
    {
        _body.Children.Clear();
        Line(_me.Holder.Name + " " + Loc.Get("leash_self_holds"), "leash-self-title");
        Line(Loc.GetF("leash_self_day", _me.Day), "leash-self-day");
        var cut = Action("leash_cut", "leash-cut", () => _ = CutAsync());
        cut.Background = F.Mint;
        cut.Foreground = F.MintInk;
        ToolTip.SetTip(cut, Loc.Get("leash_cut_tip"));
        _body.Children.Add(cut);

        int gates = _me.Pending.Count(p => p.Kind != PunishKind.Chaster);
        if (gates > 0) Line(Loc.GetF("leash_self_pending", gates), "leash-self-pending");
        if (_me.Assignment is { } a)
        {
            string what = a.Kind switch
            {
                AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", a.Size),
                AssignKind.Quests => Loc.GetF("leash_assign_quests_n", a.Size),
                _ => Loc.Get("leash_assign_video_n"),
            };
            var key = a.Status switch { AssignStatus.Done => "leash_self_task_done", AssignStatus.Missed => "leash_self_task_missed", _ => "leash_self_task_open" };
            Line(Loc.GetF(key, what, _me.Holder.Name), "leash-self-task");
            if (LeashAssignRule.OfferWatch(a))
            {
                bool running = LeashTaskHost.Runner?.RunningAid == a.Aid;
                var watch = Action(running ? "leash_self_watching" : "leash_self_watch", "leash-self-watch", () => StartWatch(a));
                watch.IsEnabled = !running;
                _body.Children.Add(watch);
            }
        }
        if (_me.Pardons > 0) Line(Loc.GetF("leash_self_pardons", _me.Pardons), "leash-self-pardons");
        if (gates == 0 && _me.Assignment == null && _me.Pardons == 0)
            Line(Loc.Get("leash_self_clear"), "leash-self-clear");

        Line(Loc.Get("leash_self_level"), "leash-self-level");
        var levels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var level in Enum.GetValues<LeashIntensity>())
        {
            var b = Action("leash_level_" + level.ToString().ToLowerInvariant(), "leash-level:" + level, () => _ = SetLevelAsync(level));
            b.BorderBrush = level == _me.Intensity ? F.Pink : F.Line2;
            levels.Children.Add(b);
        }
        _body.Children.Add(levels);
        bool dnd = Rules.DndOn(_me.DndUntil, DateTimeOffset.UtcNow);
        Line(dnd && _me.DndUntil is { } until ? Loc.GetF("leash_self_quiet_until", Rules.DndUntilText(until)) : Loc.Get("leash_self_quiet"), "leash-self-quiet");
        var quiet = new WrapPanel();
        var choices = new[] { (LeashDnd.Off, "off"), (LeashDnd.OneHour, "1h"), (LeashDnd.FourHours, "4h"), (LeashDnd.Today, "today") };
        foreach (var (value, key) in choices)
            quiet.Children.Add(Action("leash_dnd_" + key, "leash-dnd:" + value, () => _ = SetDndAsync(value)));
        _body.Children.Add(quiet);
        Line(Loc.Get("leash_video_max"), "leash-self-video-label");
        var cap = new Slider { Minimum = LeashVideoCap.Min, Maximum = LeashVideoCap.Max, Value = _me.VideoMax, TickFrequency = 1, IsSnapToTickEnabled = true, Tag = "leash-video-max" };
        var readout = F.Label(Loc.GetF("leash_video_cap", _me.VideoMax), 12, F.Text);
        readout.Tag = "leash-video-cap-readout";
        cap.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty) readout.Text = Loc.GetF("leash_video_cap", (int)cap.Value);
        };
        _body.Children.Add(readout);
        ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
        cap.PointerCaptureLost += (_, _) => _ = SetVideoMaxAsync((int)cap.Value);
        cap.KeyUp += (_, _) => _ = SetVideoMaxAsync((int)cap.Value);
        _body.Children.Add(cap);
        Line(Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(CoreSettings.Current.PanicKey)), "leash-self-hold");
        if (_me.Stickers.Count > 0)
        {
            Line(Loc.GetF("leash_self_shelf", _me.Stickers.Count), "leash-shelf");
            // ponytail: textual sticker ids until the shared sticker artwork is ported; no emoji icons.
            foreach (var s in _me.Stickers.Reverse().Take(14))
                Line(s.Id + " · " + Loc.GetF("leash_sticker_from", s.From.Name), "leash-sticker");
        }
        // ponytail: the read-only holder preview needs the unported holder card.
    }

    /// <summary>Same one-click cut as the gate (WPF LeashSurfaces.Cut).</summary>
    internal Task CutAsync() => LeashHead.CutAsync(_service(), TopLevel.GetTopLevel(this) as MainShellWindow);

    internal bool StartWatch(Assignment a)
    {
        var runner = LeashTaskHost.Runner;
        bool ok = false;
        try { ok = runner?.StartAssignmentWatch(a) == true; } catch { }
        if (!ok) App.Notifications.Show(Loc.Get(runner?.IsRunning == true ? "leash_self_watch_busy" : "leash_self_watch_cannot"), Helpers.NotificationType.Warning);
        Render();
        return ok;
    }

    internal async Task SetLevelAsync(LeashIntensity level)
    {
        if (level == _me.Intensity) return;
        _me = _me with { Intensity = level };
        Render();
        try { if (_service() is { } s) await s.SetIntensityAsync(level); } catch { }
    }

    internal async Task SetDndAsync(LeashDnd dnd)
    {
        try { if (_service() is { } s) await s.SetDndAsync(dnd); } catch { }
    }

    internal async Task SetVideoMaxAsync(int minutes)
    {
        minutes = LeashVideoCap.Clamp(minutes);
        if (minutes == _me.VideoMax) return;
        _me = _me with { VideoMax = minutes };
        try { if (_service() is { } s) await s.SetVideoMaxAsync(minutes); } catch { }
    }
}
