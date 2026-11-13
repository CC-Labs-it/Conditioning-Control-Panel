// WPF FriendsDrawer.Link.cs, with Core FriendsLinkRules owning grammar, expiry and wording.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
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
    private Button? _linkPill;
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
        return _linkPill = pill;
    }

    private TextBlock? LinkNote()
    {
        _linkNote = null;
        if (_linkNoteText == null) return null;
        var note = Wrap(Label(_linkNoteText, 10.5, _linkNoteGood ? Mint : Gold, Display));
        note.Margin = new Thickness(8, 0, 2, 4);
        note.Tag = "friends-link-note";
        // Every rebuilt note keeps the rise; only a fresh one starts hidden.
        note.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(180)) } };
        if (_linkNoteFresh)
        {
            _linkNoteFresh = false;
            note.Opacity = 0;
            Dispatcher.UIThread.Post(() => note.Opacity = 1);
        }
        return _linkNote = note;
    }

    private Control NewLinkLine()
    {
        var link = Pill(Loc.Get("friends_link_new"), Brushes.Transparent, Pink, "friends-new-link");
        link.Padding = new Thickness(0);
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
            // WPF FriendsDrawer.Link.cs:110-126: denied or click cue, then the replacement pill pops.
            if (text == null || !await CopyText(text))
            { LinkSound("denied"); SetLinkNote(Loc.Get("friends_add_try_later"), false); return false; }
            LinkSound("click");
            if (isLink)
            {
                var (key, left, uses) = FriendsLinkRules.CopiedNote(link!, LinkClock());
                SetLinkNote(Loc.GetF(key, left, uses), true);
            }
            else SetLinkNote(Loc.Get("friends_link_offline"), false);
            // Render replaced the pressed pill. Animate the replacement once it is laid out.
            Dispatcher.UIThread.Post(() => { if (_linkPill is { } pill) PlayLinkFx(pill, false); });
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
            LinkSound(good ? "click" : "denied");   // WPF FriendsDrawer.Link.cs:140
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
        // WPF FriendsDrawer.Link.cs:165-173: a claim shockwave and the accepted cue, or denied.
        LinkSound(good ? "accepted" : "denied");
        if (good) { PlayLinkFx(_addGo, true); _codeBox.Text = ""; }
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
    }

    // WPF FriendsSfx Click / Accepted / Denied, through FriendsLanding's master-volume and gap gate.
    private readonly Canvas _linkFx = new() { IsHitTestVisible = false, ClipToBounds = false };
    internal Action<string> LinkSound { get; set; } = cue => FriendsLanding.Sfx("chaos/" +
        (cue == "click" ? "ui_click.mp3" : cue == "accepted" ? "ui_unlock.mp3" : "ui_denied.mp3"),
        cue == "click" ? .12f : cue == "accepted" ? .18f : .14f, "friends-" + cue);

    internal void PlayLinkFx(Control from, bool wave, IBrush? color = null)
    {
        var k = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level switch { Models.MotionLevel.Off => 0, Models.MotionLevel.Reduced => .5, _ => 1.0 };
        if (k == 0 || TopLevel.GetTopLevel(this) == null) return; // closed drawer: a detached BeatLoop would wait in Live forever
        var fx = new LinkBurst(from, wave, k, from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), _linkFx) ?? default, color);
        _linkFx.Children.Add(fx);
        global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop? beat = null;
        beat = new(fx, seconds => { fx.Step(seconds * 1000); if (fx.Done) { beat!.Stop(); _linkFx.Children.Remove(fx); } });
        fx.DetachedFromVisualTree += (_, _) => { fx.Step(double.MaxValue); beat.Stop(); Dispatcher.UIThread.Post(() => _linkFx.Children.Remove(fx)); };
        beat.OnParked = () => { fx.Step(double.MaxValue); beat.Stop(); _linkFx.Children.Remove(fx); };
        beat.Start();
    }

    // WPF FriendsDrawer.Juice.cs: Pop 380/110 ms, 12 sparks; Shockwave two rings 120 ms apart, 14 sparks.
    internal sealed class LinkBurst : Control
    {
        private readonly Control _from; private readonly Point _center; private readonly double _k;
        internal IBrush Color { get; }
        internal bool Wave { get; } internal double Age { get; private set; }
        internal int Sparks => (int)Math.Round((Wave ? 14 : 12) * _k);
        internal bool Done => Age >= (Wave ? 620 / _k + 120 : 680);
        internal LinkBurst(Control from, bool wave, double k, Point center, IBrush? color = null)
        { _from = from; Wave = wave; _k = k; _center = center; Color = color ?? (wave ? Mint : Lilac); Effect = new DropShadowEffect { Color = ((ISolidColorBrush)Color).Color, BlurRadius = wave ? 12 : 6, Opacity = .9, OffsetX = 0, OffsetY = 0 }; Width = Height = 1; Tag = wave ? "friends-link-wave" : "friends-link-pop"; Step(0); }
        internal static double PopScale(double ms, double k) => ms >= 380 ? 1 : ms <= 110
            ? 1 + .22 * k * (1 - Math.Pow(1 - ms / 110, 2))
            : 1 + .22 * k * (1 - global::ConditioningControlPanel.Services.EmiDesk.EmiTossRules.ElasticOut((ms - 110) / 270));
        internal void Step(double ms)
        {
            Age = ms;
            if (!Wave) { var scale = PopScale(ms, _k); // the theme's press transition would smooth each beat; WPF BeginAnimation owns the scale
                if (ms < 380) _from.Transitions = null; _from.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative); _from.RenderTransform = new ScaleTransform(scale, scale); if (ms >= 380) _from.ClearValue(TransitionsProperty); }
            InvalidateVisual();
        }
        public override void Render(DrawingContext dc)
        {
            var brush = Color;
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
