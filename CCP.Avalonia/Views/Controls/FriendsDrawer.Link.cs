using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
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
    private Button LinkPill()
    {
        var pill = Pill(Loc.Get("friends_link_copy"), Raised, Text, "friends-copy-link", Line2);
        pill.Padding = new Thickness(8, 2);
        pill.Margin = new Thickness(4, 0, 0, 0);
        pill.IsEnabled = _svc?.Available == true;
        ToolTip.SetTip(pill, Loc.Get("friends_link_copy_tip"));
        pill.Click += async (_, _) => await CopyLinkAsync();
        return _linkPill = pill;
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
            { LinkSound("denied"); SetLinkNote(Loc.Get("friends_add_try_later"), false); return false; }
            LinkSound("click");
            if (isLink)
            {
                var (key, left, uses) = FriendsLinkRules.CopiedNote(link!, LinkClock());
                SetLinkNote(Loc.GetF(key, left, uses), true);
            }
            else SetLinkNote(Loc.Get("friends_link_offline"), false);
            // Render replaced the pressed pill. Animate the replacement once it is laid out.
            Dispatcher.UIThread.Post(() =>
            {
                if (_linkPill is not { } pill) return;
                PlayLinkFx(pill, false);
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
            LinkSound(good ? "click" : "denied");
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
        LinkSound(good ? "accepted" : "denied");
        if (good) { PlayLinkFx(_addGo, true); _codeBox.Text = ""; }
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
        _linkNoteTimer = DispatcherTimer.RunOnce(() =>
        {
            if (_note != null)
            {
                ((global::Avalonia.Animation.DoubleTransition)_note.Transitions![0]).Duration = TimeSpan.FromMilliseconds(Services.Safety.GentleVisuals.FadeMs(260));
                _note.Opacity = 0;
            }
            _linkNoteTimer = DispatcherTimer.RunOnce(() => { _linkNoteText = null; Render(); },
                TimeSpan.FromMilliseconds(Services.Safety.GentleVisuals.FadeMs(260)));
        }, TimeSpan.FromSeconds(4));
        Render();
    }

    private Control? LinkNote()
    {
        if (_linkNoteText == null) return null;
        var note = Tagged(Wrap(Label(_linkNoteText!, 10.5, _linkNoteGood ? Mint : Gold)), "friends-link-note");
        note.Transitions = new global::Avalonia.Animation.Transitions
        {
            new global::Avalonia.Animation.DoubleTransition { Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Services.Safety.GentleVisuals.RiseMs(180)) },
        };
        if (_noteFresh)
        {
            _noteFresh = false;
            note.Opacity = 0;
            Dispatcher.UIThread.Post(() => note.Opacity = 1);
        }
        return _note = note;
    }

    private readonly Canvas _linkFx = new() { IsHitTestVisible = false, ClipToBounds = false };
    internal Action<string> LinkSound { get; set; } = cue => FriendsLanding.Sfx("chaos/" +
        (cue == "click" ? "ui_click.mp3" : cue == "accepted" ? "ui_unlock.mp3" : "ui_denied.mp3"),
        cue == "click" ? .12f : cue == "accepted" ? .18f : .14f, "friends-" + cue);
    internal void PlayLinkFx(Control from, bool wave)
    {
        var k = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level switch { Models.MotionLevel.Off => 0, Models.MotionLevel.Reduced => .5, _ => 1.0 };
        if (k == 0) return;
        var fx = new LinkBurst(from, wave, k, from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), _linkFx) ?? default);
        _linkFx.Children.Add(fx);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => { fx.Step(watch.Elapsed.TotalMilliseconds); if (fx.Done) { timer.Stop(); _linkFx.Children.Remove(fx); } };
        timer.Start();
    }
    // WPF FriendsDrawer.Juice.cs: Pop 380/110 ms, 12 sparks; Shockwave two rings 120 ms apart, 14 sparks.
    internal sealed class LinkBurst : Control
    {
        private readonly Control _from; private readonly Point _center; private readonly double _k;
        internal bool Wave { get; } internal double Age { get; private set; }
        internal int Sparks => (int)Math.Round((Wave ? 14 : 12) * _k);
        internal bool Done => Age >= (Wave ? 620 / _k + 120 : 680);
        internal LinkBurst(Control from, bool wave, double k, Point center)
        { _from = from; Wave = wave; _k = k; _center = center; Effect = new DropShadowEffect { Color = ((ISolidColorBrush)(wave ? Mint : Lilac)).Color, BlurRadius = wave ? 12 : 6, Opacity = .9, OffsetX = 0, OffsetY = 0 }; Width = Height = 1; Tag = wave ? "friends-link-wave" : "friends-link-pop"; Step(0); }
        internal static double PopScale(double ms, double k) => ms >= 380 ? 1 : ms <= 110
            ? 1 + .22 * k * (1 - Math.Pow(1 - ms / 110, 2))
            : 1 + .22 * k * Math.Exp(-4 * (ms - 110) / 270) * Math.Cos(2.5 * Math.PI * (ms - 110) / 270);
        internal void Step(double ms)
        {
            Age = ms;
            if (!Wave) { var scale = PopScale(ms, _k); _from.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative); _from.RenderTransform = new ScaleTransform(scale, scale); }
            InvalidateVisual();
        }
        public override void Render(DrawingContext dc)
        {
            var brush = Wave ? Mint : Lilac;
            for (int i = 0; i < Sparks; i++)
            {
                double t = Math.Clamp(Age / (420 + i * 19 % 260), 0, 1), angle = i * 2.39996, distance = (24 + i * 7 % 30) * _k * (1 - (1 - t) * (1 - t));
                using (dc.PushOpacity(1 - t)) dc.DrawEllipse(brush, null, _center + new Vector(Math.Cos(angle) * distance, Math.Sin(angle) * distance + 6 * t), 1.5 + i % 3 * .5, 1.5 + i % 3 * .5);
            }
            if (Wave) for (int i = 0; i < 2; i++)
            {
                double t = Math.Clamp((Age - i * 120) / (620 / _k), 0, 1), radius = 10 * (.4 + (.6 + 9 * _k) * (1 - Math.Pow(1 - t, 3)));
                using (dc.PushOpacity(Age < i * 120 ? 0 : .95 * (1 - t))) dc.DrawEllipse(null, new Pen(brush, 2.5), _center, radius, radius);
            }
        }
    }
}
