// WPF FriendsDrawer.Link.cs, with Core FriendsLinkRules owning grammar, expiry and wording.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private bool _linkBusy, _linkNoteGood, _linkNoteFresh;
    private string? _linkNoteText;
    private DispatcherTimer? _linkNoteTimer;
    private TextBlock? _linkNote;
    internal string LinkNoteText => _linkNoteText ?? "";
    internal Func<DateTimeOffset> LinkClock { get; set; } = () => ServerClock.UtcNow;
    // Tests never write the desktop clipboard.
    internal Func<string, Task<bool>> CopyText { get; set; }

    private Button LinkPill()
    {
        var pill = Pill(Loc.Get("friends_link_copy"), Raised, Text, "friends-copy-link", Line2);
        if (pill.Content is TextBlock label) label.FontSize = 11;
        pill.Margin = new Thickness(4, 0, 0, 0);
        pill.Padding = new Thickness(8, 2, 8, 2);
        pill.IsEnabled = _svc?.Available == true;
        ToolTip.SetTip(pill, Loc.Get("friends_link_copy_tip"));
        pill.Click += async (_, _) => await CopyLinkAsync();
        return pill;
    }

    private TextBlock? LinkNote()
    {
        _linkNote = null;
        if (_linkNoteText == null) return null;
        var note = Wrap(Label(_linkNoteText, 10.5, _linkNoteGood ? Mint : Gold, Display));
        note.Margin = new Thickness(8, 0, 2, 4);
        note.Tag = "friends-link-note";
        if (_linkNoteFresh)
        {
            _linkNoteFresh = false;
            note.Opacity = 0;
            note.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(180)) } };
            Dispatcher.UIThread.Post(() => note.Opacity = 1);
        }
        return _linkNote = note;
    }

    private Control NewLinkLine()
    {
        var link = Pill(Loc.Get("friends_link_new"), Brushes.Transparent, Pink, "friends-new-link");
        link.Padding = new Thickness(0);
        link.IsEnabled = _svc?.Available == true;
        link.Click += async (_, _) => await NewLinkAsync();
        return new WrapPanel { Margin = new Thickness(2, 6, 0, 0), Tag = "friends-new-link-line", Children =
        { Label(Loc.Get("friends_link_new_line") + " ", 11.5, Muted, Display), link } };
    }

    internal async Task<bool> CopyLinkAsync()
    {
        if (_svc == null || _linkBusy) return false;
        _linkBusy = true;
        try
        {
            FriendLink? link = null;
            try { link = await _svc.GetLinkAsync(); } catch { }
            var (text, isLink) = FriendsLinkRules.CopyChoice(link, _svc.Snapshot.MyCode);
            if (text == null || !await CopyText(text)) { SetLinkNote(Loc.Get("friends_add_try_later"), false); return false; }
            if (isLink)
            {
                var (key, left, uses) = FriendsLinkRules.CopiedNote(link!, LinkClock());
                SetLinkNote(Loc.GetF(key, left, uses), true);
            }
            else SetLinkNote(Loc.Get("friends_link_offline"), false);
            return isLink;
        }
        finally { _linkBusy = false; }
    }

    internal async Task<bool> NewLinkAsync()
    {
        if (_svc == null || _linkBusy) return false;
        _linkBusy = true;
        try
        {
            FriendLink? link = null;
            try { link = await _svc.NewLinkAsync(); } catch { }
            bool good = link != null && await CopyText(link.Url);
            _addResult.Text = Loc.Get(good ? "friends_link_new_done" : "friends_add_try_later");
            _addResult.Foreground = good ? Mint : Gold;
            return good;
        }
        finally { _linkBusy = false; }
    }

    private async Task<AddResult> ClaimTypedAsync(string token)
    {
        if (_svc == null) return AddResult.TryLater;
        _addGo.IsEnabled = false;
        ClaimResult r;
        try { r = await _svc.ClaimLinkAsync(token); } catch { r = ClaimResult.TryLater; }
        bool good = FriendsLinkRules.IsGood(r);
        _addResult.Text = Loc.Get(FriendsLinkRules.ClaimResultKey(r));
        _addResult.Foreground = good ? Mint : Gold;
        if (good) _codeBox.Text = "";
        else _addGo.IsEnabled = true;
        // The Core service refreshed a successful claim. Do not spend a second state call.
        return r switch
        {
            ClaimResult.Friends => AddResult.Accepted, ClaimResult.Already => AddResult.Already,
            ClaimResult.Self => AddResult.Self, ClaimResult.Full => AddResult.Full,
            ClaimResult.Unusable => AddResult.NotFound, _ => AddResult.TryLater,
        };
    }

    private void SetLinkNote(string text, bool good)
    {
        (_linkNoteText, _linkNoteGood, _linkNoteFresh) = (text, good, true);
        _linkNoteTimer?.Stop();
        var timer = _linkNoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (_linkNote is { } note)
            {
                var duration = TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(260));
                note.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration } };
                note.Opacity = 0;
                await Task.Delay(duration);
            }
            if (_linkNoteTimer != timer) return;
            _linkNoteText = null;
            Render();
        };
        timer.Start();
        Render();
        // ponytail: FriendsDrawer's shared Pop burst and FriendsSfx remain in the drawer juice lane.
    }
}
