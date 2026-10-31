// PORTED from WPF Controls/Leash/LeashSelfCard.cs. Own-card safety and settings slice.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashSelfCard : Border
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#0B0716"));
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _body = new();
    private MyLeash _me;

    // RenderProof constructs every control in Views with a no-argument sample constructor.
    internal LeashSelfCard() : this(new MyLeash(new LeashPerson("sample", "Juno", null),
        LeashIntensity.Standard, DateTimeOffset.UtcNow.AddDays(-2), 3, null, LeashRemoteMode.Ask,
        Array.Empty<Punishment>(), null, 0, Array.Empty<Sticker>()), () => null) { }

    internal LeashSelfCard(MyLeash me, Func<ILeashService?> service)
    {
        _me = me;
        _service = service;
        Tag = "leash-self-card";
        Background = new SolidColorBrush(Color.Parse("#24102F"));
        BorderBrush = new SolidColorBrush(Color.Parse("#88FF5FB4"));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Child = _body;
        // ponytail: holder preview, sticker artwork, help and LeashFx need the remaining drawer lane.
        Render();
    }

    internal void Update(MyLeash me) { _me = me; Render(); }

    private void Render()
    {
        _body.Children.Clear();
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Label(_me.Holder.Name + " " + Loc.Get("leash_self_holds"), 16, F.Text, "leash-self-title"));
        who.Children.Add(Label(Loc.GetF("leash_self_day", _me.Day), 11, F.Pink, "leash-self-day"));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var avatar = F.Avatar(_me.Holder.Name, 40, null);
        avatar.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(avatar);
        Grid.SetColumn(who, 1); head.Children.Add(who);
        _body.Children.Add(head);

        var cut = Button(Loc.Get("leash_cut"), "leash-cut", F.Mint, F.MintInk, IconKind.Cut);
        ToolTip.SetTip(cut, Loc.Get("leash_cut_tip"));
        cut.Click += async (_, _) => await CutAsync();
        _body.Children.Add(cut); // Always first, enabled, one click, no confirmation or price.

        var waiting = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "leash-self-waiting" };
        var pending = LeashUiRules.PendingGateCount(_me.Pending);
        if (pending > 0) waiting.Children.Add(Label(Loc.GetF("leash_self_pending", pending), 12, F.Red));
        if (_me.Assignment is { } a)
        {
            var key = a.Status switch { AssignStatus.Done => "leash_self_task_done", AssignStatus.Missed => "leash_self_task_missed", _ => "leash_self_task_open" };
            var color = a.Status switch { AssignStatus.Done => F.Mint, AssignStatus.Missed => F.Dim, _ => F.Gold };
            // The task-size wording already lives in Core; no second rules copy in this head.
            var sizeKey = LeashUiRules.AssignSizeKey(a.Kind);
            var what = a.Kind == AssignKind.Video ? Loc.Get(sizeKey) : Loc.GetF(sizeKey, a.Size);
            waiting.Children.Add(Label(Loc.GetF(key, what, _me.Holder.Name), 12, color, "leash-self-assignment"));
            if (LeashAssignRule.OfferWatch(a))
            {
                var running = LeashTaskHost.Runner?.RunningAid == a.Aid;
                var watch = Button(Loc.Get(running ? "leash_self_watching" : "leash_self_watch"), "leash-self-watch", F.Gold, Ink, IconKind.Play);
                watch.IsEnabled = !running;
                watch.HorizontalAlignment = HorizontalAlignment.Left;
                watch.Click += (_, _) => StartWatch(a);
                waiting.Children.Add(watch);
            }
        }
        if (_me.Pardons > 0) waiting.Children.Add(Label(Loc.GetF("leash_self_pardons", _me.Pardons), 12, F.Lilac));
        if (waiting.Children.Count == 0) waiting.Children.Add(Label(Loc.Get("leash_self_clear"), 12, F.Muted));
        _body.Children.Add(waiting);

        _body.Children.Add(Caption(Loc.Get("leash_self_level")));
        _body.Children.Add(Segments(new[] { "leash_level_soft", "leash_level_standard", "leash_level_strict" },
            (int)_me.Intensity, "leash-level", i => _ = SetLevelAsync((LeashIntensity)i)));
        _body.Children.Add(new LeashLevelHelp(_me.Intensity, lines: true));
        var quiet = LeashUiRules.DndOn(_me.DndUntil, DateTimeOffset.UtcNow);
        _body.Children.Add(Caption(quiet && _me.DndUntil is { } u ? Loc.GetF("leash_self_quiet_until", LeashUiRules.DndUntilText(u)) : Loc.Get("leash_self_quiet"), "leash-self-quiet"));
        _body.Children.Add(Segments(new[] { "leash_dnd_off", "leash_dnd_1h", "leash_dnd_4h", "leash_dnd_today" },
            quiet ? -1 : 0, "leash-dnd", i => _ = SetDndAsync(i switch { 1 => LeashDnd.OneHour, 2 => LeashDnd.FourHours, 3 => LeashDnd.Today, _ => LeashDnd.Off })));
        var cap = new LeashCapSlider(Loc.Get("leash_video_max"), _me.VideoMax, "leash-video-max") { Margin = new Thickness(2, 10, 2, 0) };
        ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
        cap.Committed += m => _ = SetVideoMaxAsync(m);
        _body.Children.Add(cap);
        // ponytail: do not advertise WPF's panic-key hold-to-cut until this head owns that route.
    }

    internal async Task CutAsync()
    {
        if (TopLevel.GetTopLevel(this) is MainShellWindow shell) await shell.CutLeashAsync();
        else
        {
            try { LeashTaskHost.Runner?.Cancel(); } catch { }
            try { if (_service() is { } s) await s.CutAsync(); } catch { }
        }
    }

    internal async Task SetLevelAsync(LeashIntensity level)
    {
        if (_me.Intensity == level) return;
        _me = _me with { Intensity = level }; Render();
        try { if (_service() is { } s) await s.SetIntensityAsync(level); } catch { }
    }

    internal async Task SetDndAsync(LeashDnd dnd)
    {
        try { if (_service() is { } s) await s.SetDndAsync(dnd); } catch { }
    }

    internal async Task SetVideoMaxAsync(int minutes)
    {
        if (_me.VideoMax == minutes) return;
        _me = _me with { VideoMax = minutes };
        try { if (_service() is { } s) await s.SetVideoMaxAsync(minutes); } catch { }
    }

    internal bool StartWatch(Assignment a)
    {
        var runner = LeashTaskHost.Runner;
        var ok = false;
        try { ok = runner?.StartAssignmentWatch(a) == true; } catch { }
        if (!ok) App.Notifications?.Show(Loc.Get(runner?.IsRunning == true ? "leash_self_watch_busy" : "leash_self_watch_cannot"), Helpers.NotificationType.Warning);
        Render();
        return ok;
    }

    private static TextBlock Label(string text, double size, IBrush color, string? tag = null) => new()
    {
        Text = text, FontSize = size, Foreground = color, FontFamily = F.Display,
        TextWrapping = TextWrapping.Wrap, Tag = tag, Margin = new Thickness(2, 2, 0, 2),
    };
    private static TextBlock Caption(string text, string? tag = null)
    {
        var caption = Label(text, 11.5, F.Muted, tag);
        caption.Margin = new Thickness(2, 10, 0, 4); return caption;
    }
    private static Button Button(string text, string tag, IBrush bg, IBrush ink, IconKind? icon = null)
    {
        object content = icon is { } k ? F.GlyphContent(k, text, center: true) : text;
        var b = F.Pill(content, bg, ink, tag, bg);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.Padding = new Thickness(10, 7); b.CornerRadius = new CornerRadius(10);
        b.Margin = new Thickness(0, 10, 0, 0);
        return b;
    }
    private static Grid Segments(string[] keys, int selected, string tag, Action<int> pick)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", keys.Select(_ => "*"))) };
        for (var i = 0; i < keys.Length; i++)
        {
            var index = i;
            var b = Button(Loc.Get(keys[i]), tag + ":" + i, selected == i ? F.Lilac : F.Raised, selected == i ? Ink : F.Text);
            b.Margin = new Thickness(2, 0); b.Click += (_, _) => pick(index);
            Grid.SetColumn(b, i); row.Children.Add(b);
        }
        return row;
    }
}
