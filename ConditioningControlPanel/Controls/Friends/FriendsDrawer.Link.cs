using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

// Friend links (Services/Friends/CONTRACT.md "Links"). "Copy link" sits beside your code and is
// never folded away (people press it often); "New link" is the rarer knob, so it lives inside the
// Add friend box; a pasted link or token in that box claims it (instant friends).
public sealed partial class FriendsDrawer
{
    /// <summary>The line under your code after a Copy link or New link, and when it goes.</summary>
    private string? _linkNoteText;
    private bool _linkNoteGood;
    private DispatcherTimer? _linkNoteTimer;
    private bool _linkNoteFresh;

    /// <summary>The Copy link pill and the note as the last Render built them (a Render replaces both).</summary>
    private Button? _linkPill;
    private TextBlock? _linkNote;

    /// <summary>The clipboard write. The suite swaps it so it never touches the real clipboard.</summary>
    internal Func<string, bool> CopyText { get; set; } = text =>
    {
        try { Clipboard.SetText(text); return true; } catch { return false; }
    };

    /// <summary>The clock the copied note counts from. The suite pins it.</summary>
    internal Func<DateTimeOffset> LinkClock { get; set; } = () => Services.ServerClock.UtcNow;

    /// <summary>A Copy link or New link call is out: another press is ignored until it lands, so a
    /// late reply never copies a token a newer press retired.</summary>
    private bool _linkBusy;

    private Button LinkPill()
    {
        var pill = FriendsLook.Pill(Loc.Get("friends_link_copy"), FriendsLook.RaisedBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 8, new Thickness(8, 2, 8, 2), FriendsLook.HoverBrush);
        pill.Margin = new Thickness(4, 0, 0, 0);
        pill.FontSize = 11;
        pill.Tag = "friends-copy-link";
        pill.ToolTip = Loc.Get("friends_link_copy_tip");
        pill.IsEnabled = _svc?.Available == true;
        pill.Click += (_, _) => _ = CopyLinkAsync(pill);
        _linkPill = pill;
        return pill;
    }

    private TextBlock? LinkNote()
    {
        _linkNote = null;
        if (_linkNoteText == null) return null;
        var t = FriendsLook.Label(_linkNoteText, 10.5, _linkNoteGood ? FriendsLook.MintBrush : FriendsLook.GoldBrush, FriendsLook.Display);
        t.TextTrimming = TextTrimming.None;
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(8, 0, 2, 4);
        t.Tag = "friends-link-note";
        if (_linkNoteFresh)
        {
            // IN: the note fades up once when it is set, not on every redraw while it stands.
            _linkNoteFresh = false;
            t.Opacity = 0;
            t.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1,
                TimeSpan.FromMilliseconds(Services.Safety.GentleVisuals.RiseMs(180))));
        }
        _linkNote = t;
        return t;
    }

    private UIElement NewLinkLine()
    {
        var line = new TextBlock
        {
            FontFamily = FriendsLook.Body,
            FontSize = 11.5,
            Foreground = FriendsLook.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 0, 0),
            Tag = "friends-new-link-line",
        };
        line.Inlines.Add(new System.Windows.Documents.Run(Loc.Get("friends_link_new_line") + " "));
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.Get("friends_link_new")))
        {
            Foreground = FriendsLook.PinkBrush,
            Cursor = Cursors.Hand,
        };
        link.Click += (_, _) => _ = NewLinkAsync();
        line.Inlines.Add(link);
        return line;
    }

    /// <summary>Copy link: the live link onto the clipboard, or your code when there is no link to
    /// be had (offline, an old server), and the line under the code says which. Internal for the suite.</summary>
    internal async Task<bool> CopyLinkAsync(FrameworkElement? from = null)
    {
        if (_svc == null || _linkBusy) return false;
        FriendLink? link = null;
        _linkBusy = true;
        try { link = await _svc.GetLinkAsync(); } catch { }
        finally { _linkBusy = false; }
        var (text, isLink) = FriendsLinkRules.CopyChoice(link, _last.MyCode);
        if (text == null || !CopyText(text))
        {
            FriendsSfx.Denied();
            SetLinkNote(Loc.Get("friends_add_try_later"), false);
            return false;
        }
        FriendsSfx.Click();
        if (isLink)
        {
            // Worded from the link itself: the server hands back the live link with what it has left.
            var (key, left, uses) = FriendsLinkRules.CopiedNote(link!, LinkClock());
            SetLinkNote(Loc.GetF(key, left, uses), true);
        }
        else SetLinkNote(Loc.Get("friends_link_offline"), false);
        // SetLinkNote redrew the foot, so `from` is detached: pop the pill that replaced it, once laid out.
        if (from != null && _linkPill is { } pill)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (PresentationSource.FromVisual(pill) != null) Pop(pill, FriendsLook.Lilac);
            }));
        return isLink;
    }

    /// <summary>New link: retire the old one, copy the fresh one. Worded under the add box.</summary>
    internal async Task<bool> NewLinkAsync()
    {
        if (_svc == null || _linkBusy) return false;
        FriendLink? link = null;
        _linkBusy = true;
        try { link = await _svc.NewLinkAsync(); } catch { }
        finally { _linkBusy = false; }
        bool good = link != null && CopyText(link.Url);
        if (good) FriendsSfx.Click(); else FriendsSfx.Denied();
        if (_addResult != null)
        {
            _addResult.Text = Loc.Get(good ? "friends_link_new_done" : "friends_add_try_later");
            _addResult.Foreground = good ? FriendsLook.MintBrush : FriendsLook.GoldBrush;
        }
        return good;
    }

    /// <summary>A link or token in the add box: claim it and word the answer like an add.</summary>
    private async Task<AddResult> ClaimTypedAsync(string token)
    {
        if (_svc == null) return AddResult.TryLater;
        if (_addGo != null) _addGo.IsEnabled = false;
        ClaimResult r;
        try { r = await _svc.ClaimLinkAsync(token); }
        catch { r = ClaimResult.TryLater; }
        bool good = FriendsLinkRules.IsGood(r);
        if (_addResult != null)
        {
            _addResult.Text = Loc.Get(FriendsLinkRules.ClaimResultKey(r));
            _addResult.Foreground = good ? FriendsLook.MintBrush : FriendsLook.GoldBrush;
        }
        if (good)
        {
            if (_addGo != null) Shockwave(_addGo, FriendsLook.Mint);
            FriendsSfx.Accepted();
            if (_codeBox != null) _codeBox.Text = "";
            // No refresh here: FriendsService.ClaimLinkAsync already refreshed on `friends`
            // (one `state` call against the 60/min door, not two).
        }
        else
        {
            FriendsSfx.Denied();
            if (_addGo != null) _addGo.IsEnabled = true;
        }
        return r switch
        {
            ClaimResult.Friends => AddResult.Accepted,
            ClaimResult.Already => AddResult.Already,
            ClaimResult.Self => AddResult.Self,
            ClaimResult.Full => AddResult.Full,
            ClaimResult.Unusable => AddResult.NotFound,
            _ => AddResult.TryLater,
        };
    }

    private void SetLinkNote(string text, bool good)
    {
        _linkNoteText = text;
        _linkNoteGood = good;
        _linkNoteFresh = true;
        _linkNoteTimer?.Stop();
        _linkNoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        var timer = _linkNoteTimer;
        _linkNoteTimer.Tick += (_, _) =>
        {
            timer.Stop();
            // OUT: fade the note, then drop it (a newer note in the meantime keeps its own timer).
            void Drop()
            {
                if (_linkNoteTimer != timer) return;
                _linkNoteText = null;
                Render();
            }
            if (_linkNote is not { } note || PresentationSource.FromVisual(note) == null) { Drop(); return; }
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0,
                TimeSpan.FromMilliseconds(Services.Safety.GentleVisuals.FadeMs(260)));
            fade.Completed += (_, _) => Drop();
            note.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        _linkNoteTimer.Start();
        Render();
    }

    /// <summary>The line under your code, for the suite ("" when none).</summary>
    internal string LinkNoteText => _linkNoteText ?? "";
}
