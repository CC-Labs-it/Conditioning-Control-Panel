using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Announcements;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel;

/// <summary>
/// The paper promo card: a server announcement that carries a valid <c>promo</c> (7.4+). Same
/// manners as <see cref="AnnouncementPopup"/> (topmost, never steals focus, one dismissal record
/// per announcement id), drawn as the Terms &amp; Conditioning title screen. Dismiss (Not now, the
/// x, Escape) and the button answer the announcement through <see cref="AnnouncementDismissal"/>;
/// panic closes it at once without answering. Contract: handoff-1123/BF-PROMO-BRIEF.md.
/// </summary>
public partial class PromoCardWindow : Window, Services.Startup.IPassiveStartupSurface
{
    private static readonly List<PromoCardWindow> OpenCards = new();

    private readonly string _id;
    private readonly PromoCard _promo;
    private readonly Func<DateTimeOffset> _now;
    private readonly DispatcherTimer _countdownTimer;
    private bool _leaving;

    /// <param name="stickers">Pictures already in hand (the render test). Null: each sticker URL is
    /// fetched the way AnnouncementPopup fetches its image, and one that fails just does not draw.</param>
    public PromoCardWindow(string id, string title, string message, PromoCard promo,
        Func<DateTimeOffset>? now = null, IReadOnlyList<ImageSource?>? stickers = null)
    {
        InitializeComponent();
        _id = id;
        _promo = promo;
        _now = now ?? (() => ServerClock.UtcNow);
        Title = promo.Hook ?? title;

        Build(title, message);
        if (promo.EndsAt is { } end && end - _now() is var left && left > TimeSpan.Zero)
        {
            _shown = PromoCountdown.Format(left);
            DrawCountdown(left);
        }
        BuildArt(stickers);
        Sheet.SizeChanged += (_, _) => Sheet.Clip = new RectangleGeometry(new Rect(Sheet.RenderSize), 22, 22);

        _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => TickCountdown();

        Opacity = 0;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>The art: stickers, spiral, sparkles, the hook's highlighter and the ambient loop
    /// (PromoCardWindow.Art.cs). Partial, so the card stands without them.</summary>
    partial void BuildArt(IReadOnlyList<ImageSource?>? stickers);
    partial void StartArt();
    partial void StopArt();

    /// <summary>Opens a card for this announcement. Throws only if the window cannot be built; the
    /// caller then shows the old popup.</summary>
    public static void ShowFor(string id, string title, string message, PromoCard promo)
    {
        foreach (var open in OpenCards.ToList())
            if (open._id == id) { open.Leave(record: false, quick: true); }
        var card = new PromoCardWindow(id, title, message, promo);
        card.Show();
    }

    /// <summary>Panic: every open card goes at once, no motion, nothing recorded.</summary>
    internal static void CloseForPanic()
    {
        foreach (var card in OpenCards.ToList())
            card.Leave(record: false, quick: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        OpenCards.Add(this);
        FitAndCentre();
        if (_promo.EndsAt != null)
        {
            TickCountdown();
            _countdownTimer.Start();
        }
        StartArt();
        PlayIn();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        OpenCards.Remove(this);
        _countdownTimer.Stop();
        _copiedTimer?.Stop();
        StopArt();
        try { BeginAnimation(OpacityProperty, null); } catch { }
    }

    /// <summary>Shrinks the card to fit a short screen (a 768 px laptop at 125%) and centres it
    /// in the work area.</summary>
    private void FitAndCentre()
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            if (ActualHeight > wa.Height - 16)
            {
                var k = Math.Max(0.6, (wa.Height - 16) / ActualHeight);
                Root.LayoutTransform = new ScaleTransform(k, k);
                UpdateLayout();
            }
            Left = wa.Left + Math.Max(0, (wa.Width - ActualWidth) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - ActualHeight) / 2);
        }
        catch (Exception ex) { App.Logger?.Debug("Promo card placement: {E}", ex.Message); }
    }

    // ---- motion: IN and OUT ----

    private static bool Moves => MotionFx.AllowTransitions && !GentleVisuals.On;

    private void PlayIn()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        if (!Moves) return;
        var settle = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(460);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = settle });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = settle });
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, d) { EasingFunction = settle });
        var i = 0;
        foreach (var row in MenuStack.Children.OfType<FrameworkElement>())
            PopIn(row, 140 + 55 * i++);
    }

    /// <summary>Leaves: answers the announcement when <paramref name="record"/>, then fades and
    /// sinks out (or closes at once under panic, reduced motion or Gentle visuals).</summary>
    private void Leave(bool record, bool quick = false)
    {
        if (_leaving) return;
        _leaving = true;
        _countdownTimer.Stop();
        StopArt();
        if (record) AnnouncementDismissal.Record(_id);
        if (quick || !MotionFx.AllowTransitions)
        {
            try { Close(); } catch { }
            return;
        }
        try
        {
            var d = TimeSpan.FromMilliseconds(GentleVisuals.On ? 300 : 220);
            var fade = new DoubleAnimation(Opacity, 0, d);
            fade.Completed += (_, _) => { try { Close(); } catch { } };
            if (!GentleVisuals.On)
            {
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
                RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.95, d) { EasingFunction = ease });
                RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.95, d) { EasingFunction = ease });
                RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, d) { EasingFunction = ease });
            }
            BeginAnimation(OpacityProperty, fade);
        }
        catch { try { Close(); } catch { } }
    }

    // ---- countdown ----

    private string _shown = "";

    private void TickCountdown()
    {
        if (_promo.EndsAt is not { } end) return;
        var left = end - _now();
        if (left <= TimeSpan.Zero)
        {
            // The offer is over: the card goes, and the announcement counts as seen.
            Leave(record: true);
            return;
        }
        var text = PromoCountdown.Format(left);
        if (text == _shown) return;
        _shown = text;
        DrawCountdown(left);
    }

    // ---- actions ----

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Leave(record: true);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Leave(record: true);
    }

    private void OnNotNow(object sender, RoutedEventArgs e) => Leave(record: true);

    private void OnCta(object sender, RoutedEventArgs e)
    {
        // Checked again by PromoCardRules: absolute https on an allowed host, no quotes, no spaces.
        if (_promo.Cta != null) Helpers.BrowserLauncher.TryOpenUrl(_promo.Cta.Url.AbsoluteUri);
        Leave(record: true);
    }

    private DispatcherTimer? _copiedTimer;

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_promo.Code == null) return;
        var copied = false;
        for (var attempt = 0; attempt < 3 && !copied; attempt++)
        {
            // The clipboard can be held by another program for a moment.
            try { Clipboard.SetText(_promo.Code); copied = true; }
            catch (Exception ex) { App.Logger?.Debug("Promo code copy: {E}", ex.Message); }
        }
        if (!copied || _copyMeta == null) return;

        _copyMeta.Text = Localization.Loc.Get("promo_copied");
        if (Moves && _copyMeta.Parent is FrameworkElement chip) PopIn(chip, 0, from: 1.25);
        _copiedTimer ??= new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1.5) };
        _copiedTimer.Stop();
        _copiedTimer.Tick -= ResetCopied;
        _copiedTimer.Tick += ResetCopied;
        _copiedTimer.Start();
    }

    private void ResetCopied(object? sender, EventArgs e)
    {
        _copiedTimer?.Stop();
        if (_copyMeta != null) _copyMeta.Text = Localization.Loc.Get("promo_copy_code");
    }
}
