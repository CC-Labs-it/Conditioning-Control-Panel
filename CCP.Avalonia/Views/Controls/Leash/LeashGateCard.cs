// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashGateCard.cs (THE GATE, leashed side).
// The oldest pending punishment stands over the panel: "Vex says / Lines, five times", progress pips
// (or a bar), the one action, a PUNISHED stamp, the pardon button when a token is held. Panic and Cut
// leash sit on the card ALWAYS; neither is ever disabled here.
// Flat on this head (P82): the WPF glow Effects become a BoxShadow on the card and nothing on the bar;
// the stamp sits at its 10 degree rest.
// ponytail: not ported yet - the "?" explainer button (WPF LeashLook.Help(LeashExplainRole.Gate), lands
// with the explainer unit), the fade/thud/stamp swing and pip sparks (WPF LeashFx, lands with the leash
// fx unit), and the holder's avatar picture (initials only, as the friends drawer draws them).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashGateCard : Grid
{
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x0B, 0x07, 0x16));
    private Punishment? _p;
    private int _pardons;
    private int _done;
    private int _total;
    private bool _running;
    private bool _unplayable;
    private readonly Border _card = new();
    private readonly StackPanel _body = new();

    public event Action<Punishment>? StartRequested;
    public event Action<Punishment>? PardonRequested;
    public event Action? PanicRequested;
    public event Action? CutRequested;
    /// <summary>"Not now" on a video that will not play: the gate stands down (the punishment stays pending).</summary>
    public event Action? LaterRequested;

    public LeashGateCard()
    {
        IsVisible = false;
        Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x08, 0x05, 0x10));
        Tag = "leash-gate";
        _card.Width = 400;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Center;
        _card.CornerRadius = new CornerRadius(18);
        _card.Padding = new Thickness(22, 20, 22, 18);
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x5F, 0x7A));
        _card.BorderThickness = new Thickness(1);
        _card.Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(0x3A, 0x10, 0x30), 0), new GradientStop(Color.FromRgb(0x0D, 0x09, 0x14), 1) },
        };
        _card.BoxShadow = BoxShadows.Parse("0 0 30 0 #40FF5F7A");
        _card.Child = _body;
        Children.Add(_card);
    }

    public bool IsUp => IsVisible;
    public string? Pid => _p?.Pid;
    public bool ShowsUnplayable => IsUp && _unplayable;
    internal bool Running => _running;
    internal (int Done, int Total) ProgressShown => (_done, _total);

    /// <summary>Shows (or refreshes) the card for <paramref name="p"/>.</summary>
    public void Present(Punishment p, int pardons, bool unplayable = false)
    {
        bool fresh = _p?.Pid != p.Pid || !IsUp;
        if (_p?.Pid != p.Pid) { _done = 0; _total = LeashUiRules.Pips(p); _running = false; }
        _p = p;
        _pardons = pardons;
        _unplayable = unplayable;
        if (unplayable) _running = false;
        Render();
        IsVisible = true;
        if (!fresh) return;
        // The panel only presents the gate while it is up and idle: the punishment is on screen.
        try { Platform.LeashHead.Service?.NoteShown(p.Pid); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] gate seen note failed: {E}", ex.Message); }
    }

    public void Dismiss() => IsVisible = false;

    /// <summary>The runner moved: pips fill, the button relabels.</summary>
    public void SetProgress(int done, int total, bool running)
    {
        (_done, _total, _running) = (done, total, running);
        Render();
    }

    /// <summary>The runner went idle without finishing: the pips stay, the button is live again.</summary>
    public void StopRunning()
    {
        if (!_running) return;
        _running = false;
        Render();
    }

    private void Render()
    {
        _body.Children.Clear();
        if (_p is not { } p) return;

        var top = new Grid();
        var stamp = new Border
        {
            BorderBrush = F.Red, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new RotateTransform(10), Tag = "leash-gate-stamp",
            Child = F.Label(Loc.Get("leash_gate_stamp"), 12, F.Red, F.Display, FontWeight.Bold),
        };
        top.Children.Add(stamp);
        var from = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var av = F.Avatar(p.From.Name, 26, null, p.From.Look);
        av.Margin = new Thickness(0, 0, 8, 0);
        from.Children.Add(av);
        from.Children.Add(F.Label(Loc.GetF("leash_gate_says", p.From.Name), 13.5, F.Gold, null, FontWeight.SemiBold));
        top.Children.Add(from);
        _body.Children.Add(top);

        var (key, arg) = LeashUiRules.GateTitle(p);
        var big = Centered(F.Label(Loc.GetF(key, arg), 30, F.Text, F.Display, FontWeight.SemiBold), 12);
        big.Tag = "leash-gate-title";
        _body.Children.Add(big);

        if (_unplayable)
        {
            // The video would not play and the holder could not be told: say so, no Play button,
            // Pardon and Cut stay, "Not now" stands the gate down.
            var why = Centered(F.Label(Loc.GetF("leash_gate_unplayable", p.From.Name), 13, F.Gold, null, FontWeight.SemiBold), 12);
            why.Tag = "leash-gate-unplayable";
            _body.Children.Add(why);
            var later = Chunky(Loc.Get("leash_gate_later"), pink: false, null, 14, "leash-gate-later", 14);
            later.Click += (_, _) => LaterRequested?.Invoke();
            _body.Children.Add(later);
        }
        else
        {
            _body.Children.Add(ProgressView(p));
            var go = Chunky(_running ? Loc.Get("leash_gate_running") : Loc.Get(LeashUiRules.GateActionKey(p, _done)),
                pink: true, PunishIcon(p.Kind), 16, "leash-gate-go", 14);
            go.IsEnabled = !_running;
            go.Click += (_, _) => { if (_p != null) StartRequested?.Invoke(_p); };
            _body.Children.Add(go);
        }

        if (_pardons > 0)
        {
            var pardon = Chunky(Loc.GetF("leash_gate_pardon", _pardons), pink: false, IconKind.Star, 12.5, "leash-gate-pardon", 8);
            pardon.Click += (_, _) => { if (_p != null) PardonRequested?.Invoke(_p); };
            _body.Children.Add(pardon);
        }

        var hours = Math.Max(1, (int)Math.Ceiling((p.ExpiresAt - DateTimeOffset.UtcNow).TotalHours));
        var note = F.Label(Loc.GetF("leash_gate_expires", hours), 11, F.Dim);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(note);

        // The two ways out. Always here, never disabled.
        var exits = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        var panic = Exit(Loc.Get("leash_gate_panic"), F.Red, IconKind.ErrorCircle, "leash-gate-panic");
        panic.Margin = new Thickness(0, 0, 4, 0);
        panic.Click += (_, _) => PanicRequested?.Invoke();
        var cut = Exit(Loc.Get("leash_cut"), F.Mint, IconKind.Cut, "leash-gate-cut");
        cut.Margin = new Thickness(4, 0, 0, 0);
        cut.Click += (_, _) => CutRequested?.Invoke();
        exits.Children.Add(panic);
        exits.Children.Add(cut);
        _body.Children.Add(exits);
    }

    private static TextBlock Centered(TextBlock t, double top)
    {
        (t.TextWrapping, t.TextTrimming, t.TextAlignment) = (TextWrapping.Wrap, TextTrimming.None, TextAlignment.Center);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, top, 0, 0);
        return t;
    }

    private static StackPanel IconAndText(IconKind? icon, string text, double size, IBrush fg)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon is { } k)
        {
            var g = IconGlyph.Of(k, IconVariant.Filled, null, size);
            (g.Foreground, g.Margin) = (fg, new Thickness(0, 0, 6, 0));
            sp.Children.Add(g);
        }
        sp.Children.Add(F.Label(text, size, fg, F.Display, FontWeight.SemiBold));
        return sp;
    }

    /// <summary>WPF LeashLook.Chunky: a full-width rounded action (Pink = filled, Ghost = outlined).</summary>
    private static Button Chunky(string text, bool pink, IconKind? icon, double size, string tag, double top)
    {
        var fg = pink ? Ink : F.Muted;
        var b = F.Pill(IconAndText(icon, text, size, fg), pink ? F.Pink : Brushes.Transparent, fg, tag, pink ? F.Pink : F.Line2);
        (b.HorizontalAlignment, b.HorizontalContentAlignment) = (HorizontalAlignment.Stretch, HorizontalAlignment.Center);
        (b.Padding, b.CornerRadius, b.Margin) = (new Thickness(12, 9, 12, 9), new CornerRadius(12), new Thickness(0, top, 0, 0));
        return b;
    }

    private static Button Exit(string text, IBrush accent, IconKind icon, string tag)
    {
        var b = F.Pill(IconAndText(icon, text, 13, accent), new SolidColorBrush(Color.FromRgb(0x2E, 0x20, 0x46)), accent, tag, F.Line2);
        (b.HorizontalAlignment, b.HorizontalContentAlignment) = (HorizontalAlignment.Stretch, HorizontalAlignment.Center);
        (b.Padding, b.CornerRadius) = (new Thickness(8, 7, 8, 7), new CornerRadius(10));
        return b;
    }

    private Control ProgressView(Punishment p)
    {
        int n = LeashUiRules.Pips(p);
        if (n > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Tag = "leash-gate-pips" };
            double w = n > 6 ? 24 : 32;
            for (int i = 0; i < n; i++)
            {
                bool done = i < _done;
                var pip = new Border
                {
                    Width = w, Height = w * 1.3, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(2),
                    BorderBrush = done ? F.Pink : new SolidColorBrush(Color.FromRgb(0x4A, 0x35, 0x61)),
                    Background = done ? F.Pink : Brushes.Transparent, Margin = new Thickness(3, 0, 3, 0),
                    Tag = done ? "leash-pip-done" : "leash-pip",
                };
                if (done)
                {
                    var lockIcon = IconGlyph.Of(IconKind.LockClosed, IconVariant.Filled, null, w * 0.5);
                    (lockIcon.Foreground, lockIcon.HorizontalAlignment) = (Ink, HorizontalAlignment.Center);
                    pip.Child = lockIcon;
                }
                row.Children.Add(pip);
            }
            return row;
        }
        var track = new Grid { Height = 12, Margin = new Thickness(10, 16, 10, 0), Tag = "leash-gate-bar" };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x40)) });
        double frac = _total > 0 ? Math.Clamp(_done / (double)_total, 0, 1) : 0;
        track.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left, Width = 336 * frac,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(0xFF, 0x5F, 0xB4), 0), new GradientStop(Color.FromRgb(0xFF, 0xCF, 0x6B), 1) },
            },
        });
        return track;
    }

    private static IconKind PunishIcon(PunishKind k) => k switch
    {
        PunishKind.Lines => IconKind.LockClosed,
        PunishKind.Pink => IconKind.Eye,
        PunishKind.Bubbles => IconKind.BubbleMultiple,
        PunishKind.Video => IconKind.Play,
        _ => IconKind.Clock,
    };
}
