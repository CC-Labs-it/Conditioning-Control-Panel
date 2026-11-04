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
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

// WPF LeashDrawerSection.OfferRow, LeashAskCard and the leashed side of LeashSnapCard.
public sealed class LeashOfferCard : Border
{
    private readonly LeashOffer _offer;
    private readonly Func<ILeashService?> _service;
    private readonly StackPanel _ask = new() { IsVisible = false, Spacing = 6, Tag = "leash-offer-ask" };
    private readonly Button _look = new() { Content = Loc.Get("leash_offer_look"), Tag = "leash-offer-look" };
    private readonly Button _yes = new() { Content = Loc.Get("leash_ask_yes"), Tag = "leash-ask-yes" };
    private readonly Button _no = new() { Content = Loc.Get("leash_ask_no"), Tag = "leash-ask-no" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Tag = "leash-ask-error" };
    private readonly StackPanel _levels = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Border _guide = new();
    private readonly WrapPanel _allowed = new();
    private DispatcherTimer? _readTimer;
    private TimeSpan _readTime;
    private LeashIntensity _level = LeashIntensity.Standard;
    private bool _ready, _busy, _gone;
    internal Window? SnapWindow { get; private set; }

    internal LeashOfferCard() : this(new LeashOffer(new LeashPerson("sample", "Juno", null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)), () => null) { }

    internal LeashOfferCard(LeashOffer offer, Func<ILeashService?> service)
    {
        _offer = offer; _service = service;
        Tag = "leash-offer-row:" + offer.From.Id;
        Background = new SolidColorBrush(Color.Parse("#24102F"));
        BorderBrush = new SolidColorBrush(Color.Parse("#B49552"));
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(12);
        Padding = new Thickness(10); Margin = new Thickness(0, 4, 0, 4);
        var body = new StackPanel { Spacing = 8 }; Child = body;
        body.Children.Add(new TextBlock { Text = offer.From.Name + " " + Loc.Get("leash_ask_title"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_look); body.Children.Add(_ask);
        _look.Click += (_, _) => OpenAsk();
        var dismiss = new Button { Content = Loc.Get("btn_close"), Tag = "leash-ask-dismiss", HorizontalAlignment = HorizontalAlignment.Right };
        dismiss.Click += (_, _) => { if (!_busy) Dismiss(); };
        _ask.Children.Add(dismiss);
        var intro = new UniformGrid { Columns = 2, Tag = "leash-ask-intro" };
        foreach (var key in new[] { "leash_explain_step1_leashed", "leash_explain_step2_leashed", "leash_explain_step3_leashed", "leash_explain_step4_leashed" })
            intro.Children.Add(new Border { Margin = new Thickness(3), Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Background = F.Raised,
                Child = new TextBlock { Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap } });
        _ask.Children.Add(intro);
        foreach (var key in new[] { "leash_ask_p_sees", "leash_ask_p_rewards", "leash_ask_p_tasks", "leash_ask_p_cut", "leash_ask_b_level", "leash_ask_b_quiet" })
            _ask.Children.Add(new TextBlock { Text = Loc.Get(key), TextWrapping = TextWrapping.Wrap });
        _ask.Children.Add(new TextBlock { Text = Loc.Get("leash_ask_level") });
        _ask.Children.Add(_levels); _ask.Children.Add(_guide); _ask.Children.Add(_allowed);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _no, _yes } };
        _ask.Children.Add(actions); _ask.Children.Add(_error);
        _no.Click += async (_, _) => await AnswerAsync(false);
        _yes.Click += async (_, _) => await AnswerAsync(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _ask.IsVisible && !_busy) { Dismiss(); e.Handled = true; } };
        DetachedFromVisualTree += (_, _) => { _readTimer?.Stop(); _readTimer = null; };
        PaintLevel();
    }

    private void OpenAsk()
    {
        _ask.IsVisible = true; _look.IsVisible = false; _error.Text = ""; _gone = false;
        _readTime = TimeSpan.Zero;
        _ready = !LeashIntroRule.ShouldExplain(CoreSettings.Service?.Current, LeashIntroSide.Leashed);
        _yes.IsEnabled = _ready;
        ToolTip.SetTip(_yes, _ready ? null : Loc.Get("leash_explain_read_first"));
        _readTimer?.Stop();
        if (!_ready)
        {
            _readTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _readTimer.Tick += (_, _) => AdvanceReadClock();
            _readTimer.Start();
        }
    }

    // WPF LeashExplainCard.StartReadClock counts only time the explanation is visible.
    internal void AdvanceReadClock()
    {
        if (!_ask.IsEffectivelyVisible || _ready) return;
        _readTime += TimeSpan.FromMilliseconds(100);
        if (!LeashIntroRule.PutItOnEnabled(false, _readTime)) return;
        _ready = true; _readTimer?.Stop(); _readTimer = null;
        if (!_busy && !_gone) _yes.IsEnabled = true;
        ToolTip.SetTip(_yes, null);
    }

    internal void Fold() { if (!_busy) Dismiss(); }

    private void Dismiss()
    {
        _readTimer?.Stop(); _readTimer = null;
        _ask.IsVisible = false; _look.IsVisible = true;
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
        var result = LeashAnswerResult.Failed;
        try { if (_service() is { } s) result = await s.AnswerAsync(_offer.From.Id, accept, _level); } catch { }
        _busy = false; _no.IsEnabled = true;
        if (result == LeashAnswerResult.Done)
        {
            _readTimer?.Stop(); _readTimer = null;
            if (LeashIntroRule.Record(CoreSettings.Service?.Current, LeashIntroMoment.AskAnswered)) CoreSettings.Save();
            _ask.IsVisible = false; IsVisible = false;
            if (accept) ShowSnap(owner);
            return;
        }
        _gone = result == LeashAnswerResult.Gone;
        _yes.IsEnabled = _ready && !_gone;
        _error.Text = Loc.GetF(result switch { LeashAnswerResult.Gone => "leash_ask_err_gone", LeashAnswerResult.Off => "leash_ask_err_off", _ => "leash_ask_err_failed" }, _offer.From.Name);
    }

    // Independent of the offer row, which a successful answer can remove on the next poll.
    private void ShowSnap(Window? owner)
    {
        var title = new TextBlock { Text = Loc.Get("leash_snap_title"), FontSize = 24, Foreground = F.Gold, HorizontalAlignment = HorizontalAlignment.Center, Tag = "leash-snap-title" };
        var holder = new TextBlock { Text = Loc.GetF("leash_snap_holder", _offer.From.Name), TextWrapping = TextWrapping.Wrap };
        var leashed = new TextBlock { Text = Loc.GetF("leash_snap_leashed", Loc.Get("leash_you")), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = Loc.Get("leash_snap_ok"), Tag = "leash-snap-ok", HorizontalAlignment = HorizontalAlignment.Center };
        var chain = new Canvas { Width = 120, Height = 24, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-snap-chain" };
        for (int i = 0; i < 6; i++)
        {
            var link = new Ellipse { Width = 24, Height = 13, Stroke = F.Gold, StrokeThickness = 2 };
            Canvas.SetLeft(link, i * 19); Canvas.SetTop(link, 5); chain.Children.Add(link);
        }
        var figures = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        figures.Children.Add(F.Avatar(_offer.From.Name, 60, null));
        chain.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(chain, 1); figures.Children.Add(chain);
        var you = F.Avatar(Loc.Get("leash_you"), 60, null); Grid.SetColumn(you, 2); figures.Children.Add(you);
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(22), Width = 340, Children = { figures, title, holder, leashed, ok } };
        var window = new Window { Title = Loc.Get("leash_snap_title"), Tag = "leash-snap", Content = body, CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen };
        SnapWindow = window;
        ok.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { window.Close(); e.Handled = true; } };
        window.Closed += (_, _) => SnapWindow = null;
        if (CoreSettings.Current.MotionLevel != Models.MotionLevel.Off)
        {
            var scale = new ScaleTransform(0, 1) { Transitions = new Transitions { new DoubleTransition
                { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = new CubicEaseOut() } } };
            chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative); chain.RenderTransform = scale;
            window.Opened += (_, _) => Dispatcher.UIThread.Post(() => scale.ScaleX = 1);
        }
        try
        {
            if (owner?.IsVisible == true) window.Show(owner); else window.Show();
            LeashSound.Snap(DateTime.UtcNow);
        }
        catch (Exception ex) { SnapWindow = null; Serilog.Log.Debug("[Leash] snap failed: {E}", ex.Message); }
    }
}
