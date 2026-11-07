using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashAskCard and the leashed side of LeashSnapCard; LeashOfferSurfaces hosts it in the one ask window.
public sealed class LeashOfferCard : Border
{
    private readonly LeashOffer _offer;
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _ask = new() { Spacing = 6, Tag = "leash-offer-ask" };
    private readonly Button _yes = new() { Content = Loc.Get("leash_ask_yes"), Tag = "leash-ask-yes" };
    private readonly Button _no = new() { Content = Loc.Get("leash_ask_no"), Tag = "leash-ask-no" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Tag = "leash-ask-error" };
    private readonly StackPanel _levels = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Border _guide = new();
    private readonly StackPanel _intro = new() { Spacing = 6, Tag = "leash-ask-intro" };
    private readonly WrapPanel _allowed = new();
    private DispatcherTimer? _readTimer;
    private TimeSpan _readTime;
    private LeashIntensity _level = LeashIntensity.Standard;
    private bool _ready, _busy, _gone;
    internal Window? SnapWindow { get; private set; }
    internal bool ReadClockRunning => _readTimer?.IsEnabled == true;
    internal event Action? AskClosed;

    internal LeashOfferCard(LeashOffer offer, Func<ILeashService?> service)
    {
        _offer = offer; _service = service;
        Tag = "leash-ask:" + offer.From.Id;
        Background = new SolidColorBrush(Color.Parse("#24102F"));
        BorderBrush = new SolidColorBrush(Color.Parse("#B49552"));
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(12);
        Padding = new Thickness(10); Margin = new Thickness(0, 4, 0, 4);
        Child = _ask;
        _ask.Children.Add(new TextBlock { Text = offer.From.Name + " " + Loc.Get("leash_ask_title"), TextWrapping = TextWrapping.Wrap });
        var panels = new UniformGrid { Columns = 2 };
        foreach (var key in new[] { "leash_explain_step1_leashed", "leash_explain_step2_leashed", "leash_explain_step3_leashed", "leash_explain_step4_leashed" })
            panels.Children.Add(new Border { Margin = new Thickness(3), Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Background = F.Raised,
                Child = new TextBlock { Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap } });
        _intro.Children.Add(panels);
        // WPF LeashExplainCard.LinesFor (:84-92): the see and cut lines under the panels.
        _intro.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Tag = "leash-explain-line:see", Text = string.IsNullOrWhiteSpace(offer.From.Name)
            ? Loc.Get("leash_explain_leashed_see_anon") : Loc.GetF("leash_explain_leashed_see", offer.From.Name.Trim()) });
        _intro.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Tag = "leash-explain-line:cut",
            Text = Loc.GetF("leash_explain_leashed_cut", LeashHoldToCut.KeyLabel(CoreSettings.Service?.Current?.PanicKey)) });
        _ask.Children.Add(_intro);
        _ask.Children.Add(new TextBlock { Text = Loc.Get("leash_ask_level") });
        _ask.Children.Add(_levels); _ask.Children.Add(_guide); _ask.Children.Add(_allowed);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _no, _yes } };
        _ask.Children.Add(actions); _ask.Children.Add(_error);
        _no.Click += async (_, _) => await AnswerAsync(false);
        _yes.Click += async (_, _) => await AnswerAsync(true);
        // WPF LeashOverlayWindow: Escape is "later".
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Later(); e.Handled = true; } };
        DetachedFromVisualTree += (_, _) => { _readTimer?.Stop(); _readTimer = null; };
        AttachedToVisualTree += (_, _) => StartReadClock();
        // A fresh card per ask (WPF LeashSurfaces.ShowAsk), so the read clock starts at zero.
        _ready = !LeashIntroRule.ShouldExplain(CoreSettings.Service?.Current, LeashIntroSide.Leashed);
        _yes.IsEnabled = _ready;
        ToolTip.SetTip(_yes, _ready ? null : Loc.Get("leash_explain_read_first"));
        PaintLevel();
    }

    private void StartReadClock()
    {
        if (_ready || !_ask.IsVisible || _readTimer != null) return;
        _readTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _readTimer.Tick += (_, _) => AdvanceReadClock();
        _readTimer.Start();
    }

    // Count while some of the explanation is visible through all clipping viewports.
    private bool IntroOnScreen()
    {
        if (!_intro.IsEffectivelyVisible || _intro.Bounds.Width <= 0 || _intro.Bounds.Height <= 0) return false;
        var left = 0d; var top = 0d; var right = _intro.Bounds.Width; var bottom = _intro.Bounds.Height;
        foreach (var ancestor in _intro.GetVisualAncestors())
        {
            if (!ancestor.ClipToBounds && ancestor is not Window) continue;
            if (ancestor.TranslatePoint(default, _intro) is not { } p) return false;
            left = Math.Max(left, p.X); top = Math.Max(top, p.Y);
            right = Math.Min(right, p.X + ancestor.Bounds.Width); bottom = Math.Min(bottom, p.Y + ancestor.Bounds.Height);
            if (right <= left || bottom <= top) return false;
        }
        return true;
    }

    // WPF LeashExplainCard.StartReadClock counts only time the explanation is visible.
    internal void AdvanceReadClock()
    {
        if (!IntroOnScreen() || _ready) return;
        _readTime += TimeSpan.FromMilliseconds(100);
        if (!LeashIntroRule.PutItOnEnabled(false, _readTime)) return;
        _ready = true; _readTimer?.Stop(); _readTimer = null;
        if (!_busy && !_gone) _yes.IsEnabled = true;
        ToolTip.SetTip(_yes, null);
    }

    internal void Later() { if (!_busy) Dismiss(); }

    private void Dismiss()
    {
        _readTimer?.Stop(); _readTimer = null;
        var wasOpen = _ask.IsVisible;
        _ask.IsVisible = false;
        if (wasOpen) AskClosed?.Invoke();
    }

    private void PaintLevel()
    {
        _levels.Children.Clear(); _allowed.Children.Clear();
        foreach (var level in new[] { LeashIntensity.Soft, LeashIntensity.Standard, LeashIntensity.Strict })
        {
            var button = new Button { Content = Loc.Get(LeashUiRules.Key(level)), Tag = "leash-ask-level:" + (int)level,
                Background = level == _level ? F.Lilac : F.Raised, Foreground = level == _level ? F.MintInk : F.Text };
            button.Click += (_, _) => { _level = level; PaintLevel(); };
            _levels.Children.Add(button);
        }
        _guide.Child = new LeashLevelHelp(_level, lines: false, maxTable: 220);
        foreach (var kind in LeashUiRules.PunishOrder)
            _allowed.Children.Add(new TextBlock { Text = Loc.Get(LeashUiRules.Key(kind)), Margin = new Thickness(3),
                Foreground = LeashUiRules.Allowed(kind, _level) ? F.Text : F.Dim,
                Tag = "leash-allow:" + kind.ToString().ToLowerInvariant() + (LeashUiRules.Allowed(kind, _level) ? ":on" : "") });
    }

    private async Task AnswerAsync(bool accept)
    {
        if (_busy || (accept && (!_ready || _gone))) return;
        if (_gone) { Dismiss(); return; }
        _busy = true; _yes.IsEnabled = false; _no.IsEnabled = false;
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner?.Tag as string == "leash-auto-ask") owner = owner.Owner as Window;
        var result = LeashAnswerResult.Failed;
        try { if (_service() is { } s) result = await s.AnswerAsync(_offer.From.Id, accept, _level); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] answer failed: {E}", ex.Message); }
        _busy = false; _no.IsEnabled = true;
        if (result == LeashAnswerResult.Done)
        {
            _readTimer?.Stop(); _readTimer = null;
            if (LeashIntroRule.Record(CoreSettings.Service?.Current, LeashIntroMoment.AskAnswered)) CoreSettings.Save();
            _ask.IsVisible = false; IsVisible = false;
            if (accept) ShowSnap(owner);
            AskClosed?.Invoke();
            return;
        }
        _gone = result == LeashAnswerResult.Gone;
        _yes.IsEnabled = _ready && !_gone;
        _error.Text = Loc.GetF(result switch { LeashAnswerResult.Gone => "leash_ask_err_gone", LeashAnswerResult.Off => "leash_ask_err_off", _ => "leash_ask_err_failed" }, _offer.From.Name);
        LeashSound.Denied(DateTime.UtcNow);
    }

    // WPF LeashSurfaces.Me (:188): the display name, else "you".
    private static string MeName() =>
        string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? Loc.Get("leash_you") : CoreAccount.DisplayName!.Trim();

    // Independent of the offer row, which a successful answer can remove on the next poll.
    private void ShowSnap(Window? owner)
    {
        var title = new TextBlock { Text = Loc.Get("leash_snap_title"), FontSize = 26, Foreground = F.Gold, HorizontalAlignment = HorizontalAlignment.Center, Tag = "leash-snap-title" };
        var holder = new TextBlock { Text = Loc.GetF("leash_snap_holder", _offer.From.Name), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        var me = MeName();
        var leashed = new TextBlock { Text = Loc.GetF("leash_snap_leashed", me), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
        var ok = new Button { Content = Loc.Get("leash_snap_ok"), Tag = "leash-snap-ok", HorizontalAlignment = HorizontalAlignment.Center };
        var chain = new Canvas { Width = 190, Height = 24, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-snap-chain" };
        for (int i = 0; i < 10; i++)
        {
            var link = new Ellipse { Width = 24, Height = 13, Stroke = F.Gold, StrokeThickness = 2 };
            Canvas.SetLeft(link, i * 18.5); Canvas.SetTop(link, 5); chain.Children.Add(link);
        }
        var figures = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        figures.Children.Add(F.Avatar(_offer.From.Name, 68, null));
        chain.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(chain, 1); figures.Children.Add(chain);
        var you = F.Avatar(me, 68, null); Grid.SetColumn(you, 2); figures.Children.Add(you);
        // WPF LeashSnapCard.cs:57-89: 68 px avatars, a 190 px chain, the two recaps side by side.
        var recap = new UniformGrid { Columns = 2, Children = { holder, leashed } };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(22), Width = 380, Children = { figures, title, recap, ok } };
        var window = new Window { Title = Loc.Get("leash_snap_title"), Tag = "leash-snap", Content = body, CanResize = false, ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        SnapWindow = window;
        ok.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        window.Closed += (_, _) => SnapWindow = null;
        if (CoreSettings.Current.MotionLevel != Models.MotionLevel.Off)
        {
            var scale = new ScaleTransform(0, 1) { Transitions = new Transitions { new DoubleTransition
                { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = new SplineEasing(0.2, 1.5, 0.4, 1) } } };
            chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative); chain.RenderTransform = scale;
            title.Opacity = 0;
            // WPF LeashSnapCard.Play (:123,:136): BezierEase.Thud chain, title after 300 ms.
            window.Opened += (_, _) => { Dispatcher.UIThread.Post(() => scale.ScaleX = 1); DispatcherTimer.RunOnce(() => title.Opacity = 1, TimeSpan.FromMilliseconds(300)); };
        }
        try
        {
            if (owner?.IsVisible == true) window.Show(owner); else window.Show();
            LeashSound.Snap(DateTime.UtcNow);
        }
        catch (Exception ex) { SnapWindow = null; Serilog.Log.Debug("[Leash] snap failed: {E}", ex.Message); }
    }
}
