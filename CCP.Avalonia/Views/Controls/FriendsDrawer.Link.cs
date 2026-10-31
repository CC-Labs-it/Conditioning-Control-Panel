using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

// Friend links share Core's grammar, account cache and wire. Clipboard stays in the head.
public sealed partial class FriendsDrawer
{
    private bool _linkBusy, _linkNoteGood;
    private string? _linkNoteText;
    private TextBlock? _note;
    private bool _noteFresh;
    private IDisposable? _linkNoteTimer;
    private Button? _linkPill;
    internal string LinkNoteText => _linkNoteText ?? "";
    internal Func<DateTimeOffset> LinkClock { get; set; } = () => Services.ServerClock.UtcNow;
    internal Func<string, Task<bool>>? CopyText { get; set; }

    private Control LinkPill()
    {
        var pill = Pill(Loc.Get("friends_link_copy"), Raised, Text, "friends-copy-link", Line2);
        pill.Padding = new Thickness(8, 2);
        pill.IsEnabled = _svc?.Available == true;
        ToolTip.SetTip(pill, Loc.Get("friends_link_copy_tip"));
        pill.Click += async (_, _) => await CopyLinkAsync();
        _linkPill = pill;
        return new Border { Child = pill, Margin = new Thickness(4, 0, 0, 0), Tag = "friends-link-pop" };
    }

    private Control NewLinkLine()
    {
        var link = Pill(Loc.Get("friends_link_new"), Brushes.Transparent, Pink, "friends-new-link");
        link.Click += async (_, _) => await NewLinkAsync();
        return new WrapPanel { Tag = "friends-new-link-line", Children =
            { Wrap(Label(Loc.Get("friends_link_new_line"), 11.5, Muted)), link } };
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
            if (text == null || !await WriteClipboard(text))
            { SetLinkNote(Loc.Get("friends_add_try_later"), false); return false; }
            if (isLink)
            {
                var (key, left, uses) = FriendsLinkRules.CopiedNote(link!, LinkClock());
                SetLinkNote(Loc.GetF(key, left, uses), true);
            }
            else SetLinkNote(Loc.Get("friends_link_offline"), false);
            // Render replaced the pressed pill. Animate the replacement once it is laid out.
            Dispatcher.UIThread.Post(() =>
            {
                if (_linkPill is { } pill && TopLevel.GetTopLevel(pill) != null) StartLinkFeedback((Control)pill.Parent!);
            });
            return isLink;
        }
        finally { _linkBusy = false; }
    }

    private async Task<bool> WriteClipboard(string text)
    {
        try
        {
            if (CopyText != null) return await CopyText(text);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return false;
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch { return false; }
    }

    internal async Task<bool> NewLinkAsync()
    {
        if (_svc == null || _linkBusy) return false;
        _linkBusy = true;
        try
        {
            FriendLink? link = null;
            try { link = await _svc.NewLinkAsync(); } catch { }
            bool good = link != null && await WriteClipboard(link.Url);
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
        ClaimResult result;
        try { result = await _svc.ClaimLinkAsync(token); } catch { result = ClaimResult.TryLater; }
        bool good = FriendsLinkRules.IsGood(result);
        _addResult.Text = Loc.Get(FriendsLinkRules.ClaimResultKey(result));
        _addResult.Foreground = good ? Mint : Gold;
        if (good) _codeBox.Text = "";
        else _addGo.IsEnabled = true;
        return result switch
        {
            ClaimResult.Friends => AddResult.Accepted, ClaimResult.Already => AddResult.Already,
            ClaimResult.Self => AddResult.Self, ClaimResult.Full => AddResult.Full,
            ClaimResult.Unusable => AddResult.NotFound, _ => AddResult.TryLater,
        };
    }

    private void SetLinkNote(string text, bool good)
    {
        (_linkNoteText, _linkNoteGood, _noteFresh) = (text, good, true);
        _linkNoteTimer?.Dispose();
        _linkNoteTimer = DispatcherTimer.RunOnce(FadeLinkNote, TimeSpan.FromSeconds(4));
        Render();
    }

    internal void FadeLinkNote()
    {
        if (_note != null) { SetNoteTransition(_note, GentleVisuals.FadeMs(260)); _note.Opacity = 0; }
        _linkNoteTimer = DispatcherTimer.RunOnce(() => { _linkNoteText = null; Render(); },
            TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(260)));
    }

    private static void SetNoteTransition(Control note, int ms) => note.Transitions = new global::Avalonia.Animation.Transitions
    {
        new global::Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
    };

    private Control LinkNote()
    {
        var note = Tagged(Wrap(Label(_linkNoteText!, 10.5, _linkNoteGood ? Mint : Gold)), "friends-link-note");
        SetNoteTransition(note, GentleVisuals.RiseMs(180));
        if (_noteFresh)
        {
            _noteFresh = false;
            note.Opacity = 0;
            Dispatcher.UIThread.Post(() => note.Opacity = 1);
        }
        return _note = note;
    }

    private static void AddPlatformMarks(StackPanel line, Friend friend)
    {
        foreach (var (platform, _, tip) in global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.PlatformMarks(friend))
        {
            var mark = new IconGlyph { Kind = platform switch
            {
                FriendPlatform.Phone => FluentIcons.Common.Symbol.Phone,
                FriendPlatform.Web => FluentIcons.Common.Symbol.Globe,
                _ => FluentIcons.Common.Symbol.Desktop,
            }, Size = 11, Foreground = Dim, Margin = new Thickness(4, 0, 0, 0), Tag = "friends-platform:" + platform };
            ToolTip.SetTip(mark, Loc.Get(tip));
            line.Children.Add(mark);
        }
    }
}
