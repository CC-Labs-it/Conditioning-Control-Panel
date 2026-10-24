// Adapted from WPF 018075f1c and 5d7c2110a. Reuses the already-merged Core link rules/service.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private string? _linkNoteText;
    private bool _linkNoteGood, _linkNoteFresh;
    private DispatcherTimer? _linkNoteTimer;
    private Button? _linkPill;
    private TextBlock? _linkNote;

    // Async clipboard seam: tests never access the desktop clipboard.
    internal Func<string, Task<bool>>? CopyText { get; set; }
    private async Task<bool> CopyLinkText(string text)
    {
        if (CopyText != null) return await CopyText(text);
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch { return false; }
    }

    private Button LinkPill()
    {
        var pill = Pill(Loc.Get("friends_link_copy"), Raised, Text, "friends-copy-link", Line2);
        pill.Margin = new Thickness(4, 0, 0, 0);
        pill.Padding = new Thickness(8, 2, 8, 2);
        if (pill.Content is TextBlock t) t.FontSize = 11;
        ToolTip.SetTip(pill, Loc.Get("friends_link_copy_tip"));
        pill.IsEnabled = _svc?.Available == true;
        pill.Click += (_, _) => _ = CopyLinkAsync(pill);
        return _linkPill = pill;
    }

    private TextBlock? LinkNote()
    {
        _linkNote = null;
        if (_linkNoteText == null) return null;
        var note = Wrap(Label(_linkNoteText, 10.5, _linkNoteGood ? Mint : Gold, Display));
        note.Tag = "friends-link-note";
        note.Margin = new Thickness(8, 0, 2, 4);
        if (_linkNoteFresh)
        {
            _linkNoteFresh = false;
            _ = FadeLinkNote(note, 0, 1, GentleVisuals.RiseMs(180));
        }
        return _linkNote = note;
    }

    private Control NewLinkLine()
    {
        var link = Pill(Loc.Get("friends_link_new"), Brushes.Transparent, Pink, "friends-new-link");
        link.Padding = new Thickness(0);
        link.Click += (_, _) => _ = NewLinkAsync();
        return new WrapPanel { Tag = "friends-new-link-line", Margin = new Thickness(2, 6, 0, 0),
            Children = { Wrap(Label(Loc.Get("friends_link_new_line") + " ", 11.5, Muted)), link } };
    }

    internal async Task<bool> CopyLinkAsync(Control? from = null)
    {
        if (_svc == null) return false;
        FriendLink? link = null;
        try { link = await _svc.GetLinkAsync(); } catch { }
        var (text, isLink) = FriendsLinkRules.CopyChoice(link, _svc.Snapshot.MyCode);
        bool copied = false;
        try { if (text != null) copied = await CopyLinkText(text); } catch { }
        if (!copied) { SetLinkNote(Loc.Get("friends_add_try_later"), false); return false; }
        SetLinkNote(Loc.Get(isLink ? "friends_link_copied" : "friends_link_offline"), isLink);
        // SetLinkNote replaced the foot. Animate the replacement visual, never the detached source.
        if (from != null && _linkPill is { } pill)
            Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(pill) == null || CoreSettings.Current.MotionLevel == MotionLevel.Off) return;
                double amount = CoreSettings.Current.MotionLevel == MotionLevel.Reduced || GentleVisuals.On ? 0.4 : 1;
                // The button theme's transform transition would snapshot the mutable scale at 1.
                pill.Transitions = null;
                var ease = new QuadraticEaseOut();
                var scale = new ScaleTransform(1, 1);
                pill.RenderTransform = scale;
                BeatLoop? pop = null;
                pop = new BeatLoop(pill, seconds =>
                {
                    double ms = seconds * 1000;
                    double progress = ms <= 110 ? ms / 110 : 1 - (ms - 110) / 270;
                    double value = 1 + 0.22 * amount * ease.Ease(Math.Clamp(progress, 0, 1));
                    scale.ScaleX = scale.ScaleY = value;
                    if (ms >= 380) pop!.Stop();
                });
                pop.OnParked = pop.Stop;
                pop.Start();
            }, DispatcherPriority.Background);
        // ponytail: FriendsSfx and the shared drawer juice host are absent on this head.
        return isLink;
    }

    internal async Task<bool> NewLinkAsync()
    {
        if (_svc == null) return false;
        FriendLink? link = null;
        try { link = await _svc.NewLinkAsync(); } catch { }
        bool good = false;
        try { if (link != null) good = await CopyLinkText(link.Url); } catch { }
        _addResult.Text = Loc.Get(good ? "friends_link_new_done" : "friends_add_try_later");
        _addResult.Foreground = good ? Mint : Gold;
        return good;
    }

    private async Task<AddResult> ClaimTypedAsync(string token)
    {
        if (_svc == null) return AddResult.TryLater;
        _addGo.IsEnabled = false;
        ClaimResult result;
        try { result = await _svc.ClaimLinkAsync(token); } catch { result = ClaimResult.TryLater; }
        bool good = FriendsLinkRules.IsGood(result);
        _addResult.Text = Loc.Get(FriendsLinkRules.ClaimResultKey(result));
        _addResult.Foreground = good ? Mint : Gold;
        if (good) _codeBox.Text = "";
        else _addGo.IsEnabled = true;
        // The service refreshes on Friends; do not spend a second state request here.
        return result switch {
            ClaimResult.Friends => AddResult.Accepted, ClaimResult.Already => AddResult.Already,
            ClaimResult.Self => AddResult.Self, ClaimResult.Full => AddResult.Full,
            ClaimResult.Unusable => AddResult.NotFound, _ => AddResult.TryLater };
    }

    private void SetLinkNote(string text, bool good)
    {
        (_linkNoteText, _linkNoteGood, _linkNoteFresh) = (text, good, true);
        _linkNoteTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _linkNoteTimer = timer;
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (_linkNote is { } note && TopLevel.GetTopLevel(note) != null)
                await FadeLinkNote(note, 1, 0, GentleVisuals.FadeMs(260));
            if (_linkNoteTimer != timer) return;
            _linkNoteText = null;
            Render();
        };
        timer.Start();
        Render();
    }

    private static Task FadeLinkNote(TextBlock note, double from, double to, int ms)
    {
        var fade = new Animation { Duration = TimeSpan.FromMilliseconds(ms) };
        fade.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } });
        fade.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } });
        return fade.RunAsync(note);
    }
    internal string LinkNoteText => _linkNoteText ?? "";
}
