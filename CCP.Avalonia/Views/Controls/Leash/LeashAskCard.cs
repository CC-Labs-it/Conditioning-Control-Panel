// PORTED from WPF Controls/Leash/LeashAskCard.cs + Explain/LeashExplainCard.cs (Grid layout, read clock)
// + Explain/LeashExplainer.cs LeashAskIntro.Answered.
// ponytail: the explainer's drawn art and entrance (LeashExplainArt), LeashFx sounds/pops, the "?" button and
// WPF's hold-the-panic-key cut line are wire-views-tab-leash-b-next; the cut line says the one-click Cut this head has.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>THE ASK, leashed side: "Juno wants to hold your leash", the four-step explainer and two
/// lines, the intensity switch with the punishments it allows lit, then "Not now" and "Put it on".</summary>
public sealed class LeashAskCard : Border
{
    internal static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1B, 0x10, 0x26));
    internal static readonly IBrush GoldDeep = new SolidColorBrush(Color.FromRgb(0x6B, 0x53, 0x20));
    internal static readonly IBrush Card = Gradient(0x2A1D12, 0x1F1528);
    private static readonly IBrush Glass = Gradient(0x241742, 0x1C1233), Chip = Rgb(0x24, 0x18, 0x38);

    private readonly LeashOffer _offer;
    private readonly Func<ILeashService?> _svc;
    private readonly StackPanel _body = new();
    private readonly Border _switchHost = new();
    private readonly WrapPanel _allowed = new() { Margin = new Thickness(0, 8, 0, 0), Tag = "leash-ask-allowed" };
    private readonly TextBlock _error;
    private LeashIntensity _level = LeashIntensity.Standard;
    private DispatcherTimer? _readTimer;
    private TimeSpan _visible;
    private bool _busy, _gone;

    /// <summary>Raised after the answer went through: true = accepted.</summary>
    public event Action<LeashOffer, bool, LeashIntensity>? Answered;

    /// <summary>Raised on a close without an answer (Escape, or "Not now" on an offer that is gone).</summary>
    public event Action? Dismissed;

    // RenderProof: a sample offer, no service.
    internal LeashAskCard() : this(new LeashOffer(new LeashPerson("sample", "Juno", null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)), () => null) { }

    internal LeashAskCard(LeashOffer offer, Func<ILeashService?> service)
    {
        _offer = offer;
        _svc = service;
        Width = 380;
        Background = Glass;
        BorderBrush = GoldDeep;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        BoxShadow = BoxShadows.Parse("0 10 34 0 #99000000");
        Tag = "leash-ask:" + offer.From.Id;
        Child = _body;

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var av = F.Avatar(offer.From.Name, 60, null);
        av.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(av);
        var t = new TextBlock
        {
            FontFamily = F.Display, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = F.Text,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-ask-title",
            Inlines = new InlineCollection { new Run(offer.From.Name) { Foreground = F.Gold }, new Run(" " + Loc.Get("leash_ask_title")) },
        };
        Grid.SetColumn(t, 1);
        head.Children.Add(t);
        _body.Children.Add(head);
        _body.Children.Add(Explainer(offer.From.Name));

        _body.Children.Add(Caption(Loc.Get("leash_ask_level")));
        _body.Children.Add(_switchHost);
        _body.Children.Add(_allowed);
        PaintLevel();

        var acts = new Grid { Margin = new Thickness(0, 16, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,1.3*") };
        var no = Chunky(new TextBlock { Text = Loc.Get("leash_ask_no"), FontFamily = F.Display, FontSize = 15 }, Chip, F.Text, "leash-ask-no");
        no.Margin = new Thickness(0, 0, 5, 0);
        no.Click += async (_, _) => await AnswerAsync(false);
        PutItOnButton = Chunky(F.GlyphContent(IconKind.Link, Loc.Get("leash_ask_yes"), center: true), F.Gold, Ink, "leash-ask-yes");
        PutItOnButton.Margin = new Thickness(5, 0, 0, 0);
        PutItOnButton.Click += async (_, _) => await AnswerAsync(true);
        Grid.SetColumn(PutItOnButton, 1);
        acts.Children.Add(no);
        acts.Children.Add(PutItOnButton);
        _body.Children.Add(acts);

        _error = new TextBlock
        {
            FontSize = 12, Foreground = F.Gold, FontWeight = FontWeight.SemiBold, FontFamily = F.Display, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(2, 10, 2, 0), IsVisible = false, Tag = "leash-ask-error",
        };
        _body.Children.Add(_error);

        // WPF LeashAskCard.Build: the first ask keeps "Put it on" off until the explainer was on screen long enough.
        if (LeashIntroRule.ShouldExplain(CoreSettings.Current, LeashIntroSide.Leashed))
        {
            PutItOnButton.IsEnabled = false;
            ToolTip.SetTip(PutItOnButton, Loc.Get("leash_explain_read_first"));
            AttachedToVisualTree += (_, _) => StartReadClock();
            DetachedFromVisualTree += (_, _) => { _readTimer?.Stop(); _readTimer = null; };
        }
        else IsReadyForAnswer = true;
    }

    internal LeashIntensity Level => _level;
    internal Button PutItOnButton { get; }
    internal bool IsReadyForAnswer { get; private set; }

    /// <summary>The reason the last answer did not go through, or null.</summary>
    internal string? ErrorShown => _error.IsVisible ? _error.Text : null;

    /// <summary>What the card says when an answer did not go through. Null = it went through.</summary>
    internal static string? ErrorKey(LeashAnswerResult r) => r switch
    {
        LeashAnswerResult.Done => null,
        LeashAnswerResult.Gone => "leash_ask_err_gone",
        LeashAnswerResult.Off => "leash_ask_err_off",
        _ => "leash_ask_err_failed",
    };

    // ---- the read clock (WPF LeashExplainCard.StartReadClock) ---------------------------------

    private const int ReadTickMs = 100;

    private void StartReadClock()
    {
        if (IsReadyForAnswer || _readTimer != null) return;
        _readTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReadTickMs) };
        _readTimer.Tick += (_, _) => ReadTick(TimeSpan.FromMilliseconds(ReadTickMs));
        _readTimer.Start();
    }

    /// <summary>One clock step. Time only counts while the card is actually on screen.</summary>
    internal void ReadTick(TimeSpan step)
    {
        if (IsReadyForAnswer) return;
        if (IsEffectivelyVisible) _visible += step;
        if (!LeashIntroRule.PutItOnEnabled(false, _visible)) return;
        _readTimer?.Stop();
        _readTimer = null;
        IsReadyForAnswer = true;
        PutItOnButton.IsEnabled = true;
        ToolTip.SetTip(PutItOnButton, null);
    }

    // ---- intensity ----------------------------------------------------------------------------

    private void PaintLevel()
    {
        var keys = new[] { "leash_level_soft", "leash_level_standard", "leash_level_strict" };
        var g = new UniformGrid { Columns = keys.Length };
        for (var i = 0; i < keys.Length; i++)
        {
            var idx = i;
            var on = i == (int)_level;
            var b = Chunky(new TextBlock { Text = Loc.Get(keys[i]), FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                on ? Rgb(0x3A, 0x28, 0x5C) : Brushes.Transparent, on ? F.Text : F.Muted, "leash-ask-level:" + i + (on ? ":on" : ""));
            b.BorderBrush = on ? F.Line2 : Brushes.Transparent;
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(4, 6);
            b.Margin = new Thickness(i == 0 ? 0 : 1.5, 0, i == keys.Length - 1 ? 0 : 1.5, 0);
            b.Click += (_, _) => SetLevel((LeashIntensity)idx);
            g.Children.Add(b);
        }
        _switchHost.Child = new Border { Background = Chip, CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Tag = "leash-ask-level", Child = g };
        _allowed.Children.Clear();
        foreach (var k in LeashUiRules.PunishOrder)
        {
            var on = LeashUiRules.Allowed(k, _level);
            _allowed.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1),
                BorderBrush = on ? Rgb(0x6B, 0x27, 0x48) : Rgb(0x3A, 0x2B, 0x54),
                Background = on ? Rgb(0x3A, 0x1A, 0x33) : Chip,
                Tag = "leash-allow:" + k.ToString().ToLowerInvariant() + (on ? ":on" : ""),
                Child = F.Label(Loc.Get(LeashUiRules.Key(k)), 11.5, on ? F.Text : F.Dim, null, FontWeight.SemiBold),
            });
        }
    }

    internal void SetLevel(LeashIntensity level)
    {
        _level = level;
        PaintLevel();
    }

    // ---- the answer ---------------------------------------------------------------------------

    /// <summary>WPF AnswerAsync: only a reply the server really took closes the card; anything else keeps
    /// it open and says why. An offer that is gone leaves "Not now" as a plain close.</summary>
    internal async Task AnswerAsync(bool accept)
    {
        if (_busy) return;
        if (_gone) { Dismissed?.Invoke(); return; }
        _busy = true;
        var wasEnabled = PutItOnButton.IsEnabled;
        PutItOnButton.IsEnabled = false;
        var result = LeashAnswerResult.Failed;
        try { if (_svc() is { } s) result = await s.AnswerAsync(_offer.From.Id, accept, _level); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] answer failed: {E}", ex.Message); }
        _busy = false;
        PutItOnButton.IsEnabled = wasEnabled && result != LeashAnswerResult.Gone;
        if (ErrorKey(result) is { } key)
        {
            Serilog.Log.Information("[Leash] answer to {From} did not go through: {R}", _offer.From.Id, result);
            _gone = result == LeashAnswerResult.Gone;
            _error.Text = Loc.GetF(key, _offer.From.Name);
            _error.IsVisible = true;
            return;
        }
        // WPF LeashAskIntro.Answered: the leashed side has now had its look.
        if (LeashIntroRule.Record(CoreSettings.Current, LeashIntroMoment.AskAnswered))
            try { CoreSettings.Save(); } catch { }
        Answered?.Invoke(_offer, accept, _level);
    }

    internal void Dismiss() => Dismissed?.Invoke();

    // ---- the explainer (WPF LeashExplainCard, Grid layout) ------------------------------------

    private static Control Explainer(string name)
    {
        var root = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Tag = "leash-explain-card:leashed" };
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        var art = new[] { IconKind.PersonAdd, IconKind.Link, IconKind.Eye, IconKind.Cut };
        for (var i = 0; i < 4; i++) grid.Children.Add(Panel(i + 1, art[i], Loc.Get($"leash_explain_step{i + 1}_leashed"), i == 3));
        root.Children.Add(grid);
        var see = string.IsNullOrWhiteSpace(name) ? Loc.Get("leash_explain_leashed_see_anon") : Loc.GetF("leash_explain_leashed_see", name.Trim());
        var lines = new StackPanel { Margin = new Thickness(6, 8, 6, 0) };
        lines.Children.Add(Bullet(IconKind.Eye, F.Lilac, see, "leash-explain-line:see"));
        lines.Children.Add(Bullet(IconKind.Cut, F.Mint, Loc.Get("leash_ask_p_cut"), "leash-explain-line:cut"));
        root.Children.Add(lines);
        return root;
    }

    private static Border Panel(int number, IconKind art, string caption, bool safety)
    {
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        body.Children.Add(new IconGlyph { Kind = art, Size = 40, Foreground = safety ? F.Mint : F.Lilac, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4) });
        var cap = new TextBlock
        {
            Text = caption, FontFamily = F.Display, FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = safety ? F.Mint : F.Text,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 2, 4, 6), Tag = "leash-explain-caption",
        };
        Grid.SetRow(cap, 1);
        body.Children.Add(cap);
        body.Children.Add(new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = safety ? F.Mint : F.Line2,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 6, 0, 0),
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(number % 2 == 0 ? 6 : -6),
            Child = new TextBlock
            {
                Text = number.ToString(), FontFamily = F.Mono, FontSize = 10.5, FontWeight = FontWeight.Bold,
                Foreground = safety ? F.MintInk : F.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });
        return new Border
        {
            Child = body, CornerRadius = new CornerRadius(14), Background = Glass,
            BorderBrush = safety ? new SolidColorBrush(Color.FromArgb(0x99, 0x5F, 0xFF, 0xD0)) : F.Line,
            BorderThickness = new Thickness(safety ? 1.5 : 1), Margin = new Thickness(3), Tag = "leash-explain-panel:" + number,
        };
    }

    private static Grid Bullet(IconKind kind, IBrush fg, string text, string tag)
    {
        var g = new Grid { Margin = new Thickness(0, 3), Tag = tag, ColumnDefinitions = new ColumnDefinitions("26,*") };
        g.Children.Add(new IconGlyph { Kind = kind, Size = 16, Foreground = fg, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 1, 0, 0) });
        var t = new TextBlock { Text = text, FontSize = 13, Foreground = F.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // ---- paint helpers ------------------------------------------------------------------------

    private static TextBlock Caption(string text)
    {
        var c = F.Label(text, 11.5, F.Muted, F.Display);
        c.Margin = new Thickness(2, 14, 0, 5);
        return c;
    }

    private static Button Chunky(Control content, IBrush bg, IBrush fg, string tag)
    {
        var b = F.Pill(content, bg, fg, tag, bg);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Padding = new Thickness(12, 9);
        b.CornerRadius = new CornerRadius(12);
        return b;
    }

    private static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    private static IBrush Gradient(uint top, uint bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromUInt32(0xFF000000 | top), 0), new GradientStop(Color.FromUInt32(0xFF000000 | bottom), 1) },
    };
}
