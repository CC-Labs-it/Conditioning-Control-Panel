using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Invites;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Funnel;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Vault;

namespace ConditioningControlPanel;

/// <summary>
/// The vault gate card: what a free account sees after clicking a padlocked feature, in place of
/// the old jump to Settings · Account. Three faces in one borderless window:
/// <list type="bullet">
/// <item><b>Offer</b> - the clicked feature's art and tagline, what the tier holds, the price,
/// the supporter count when the server gives one, and the ways in: open the tier, redeem an
/// invite code (only once the server has invites), or sign in as an existing supporter.</item>
/// <item><b>Compare</b> - Basic and Prime as two tickets side by side (VaultGateDialog.Compare.cs).</item>
/// <item><b>Ending</b> - the last-day card of an invite week.</item>
/// </list>
/// Pure decisions live in <see cref="VaultOffer"/>; this class only paints and routes.
/// </summary>
public sealed partial class VaultGateDialog : Window
{
    private static readonly Brush CardBg = Frozen("#FF151528");
    private static readonly Brush RowBg = Frozen("#252542");
    private static readonly Brush Edge = Frozen("#3D3D60");
    private static readonly Brush Gold = Frozen("#E5C76B");
    private static readonly Brush Good = Frozen("#7FD8A6");
    private static readonly Brush Warn = Frozen("#FFB347");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Muted = Frozen("#B8B1CC");
    private static readonly Brush Dim = Frozen("#8079A3");

    /// <summary>Last supporter count read this run; the card never waits on it.</summary>
    private static int? _supporters;
    private static DateTime _supportersReadUtc = DateTime.MinValue;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Whether /v2/invites/mine answered, remembered for ten minutes so a padlock click
    /// costs no round trip of its own.</summary>
    private static bool? _invitesReachable;
    private static DateTime _invitesReadUtc = DateTime.MinValue;

    /// <summary>The live first-month sale: the plan list's <c>sale</c> when the list came from the
    /// server, else /config/vault-sale read at most every five minutes. Null (normal prices) when
    /// there is none or the read failed.</summary>
    private static VaultSaleInfo? _sale;
    private static DateTime _saleReadUtc = DateTime.MinValue;

    /// <summary>The card on screen, if any. One at a time: a second padlock or toast replaces it.</summary>
    private static VaultGateDialog? _open;

    private readonly Border _card = new();
    private readonly StackPanel _body = new();
    private readonly ExclusiveFeature? _feature;
    private readonly int _tier;
    private readonly Action _signIn;

    /// <summary>The feature key the funnel counters carry for this card ("vault" for the generic
    /// card, "invite_ending" for the last-day card).</summary>
    private string _funnelKey = "vault";
    /// <summary>The price list the card paints from: the cached plan list at open, replaced when a
    /// fresh read lands (<see cref="FillPlansAsync"/>). Buttons read it at click time.</summary>
    private PlanSet _plans = PlanCatalog.Current;
    private PriceCurrency _currency => VaultOffer.HeadlineCurrency(_plans);

    /// <summary>Every price on the face showing, so a sale that lands late can repaint them.</summary>
    private readonly List<(StackPanel Slot, int Tier, bool Compact, bool Yearly, bool Hints)> _priceSlots = new();

    /// <summary>The compare face's billing switch. Yearly is Patreon-only, and says so.</summary>
    private bool _yearly;

    private VaultGateDialog(ExclusiveFeature? feature, int tier, Action signIn)
    {
        _feature = feature;
        _tier = Math.Clamp(tier, 1, 2);
        _signIn = signIn;

        Title = "CC Labs";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _card.Background = CardBg;
        _card.CornerRadius = new CornerRadius(14);
        _card.BorderThickness = new Thickness(1.5);
        _card.SetResourceReference(Border.BorderBrushProperty, "PinkBrush");
        _card.Margin = new Thickness(12);
        _card.Padding = new Thickness(22, 20, 22, 18);
        _card.Effect = _glow = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0x69, 0xB4), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.45 };
        _card.Child = _body;
        Content = _card;
        SizeChanged += OnFaceResized;

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // DragMove captures the mouse and eats the button-up, so a press on a clickable piece (every
        // link and pill wears the hand cursor) must not start a drag or its MouseLeftButtonUp never fires.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && !OnClickable(e.OriginalSource as DependencyObject)) try { DragMove(); } catch (InvalidOperationException) { } }; // swallow: DragMove refuses once the button is up
    }

    // ============================== entry points ==============================

    /// <summary>The gate card for a feature key (null = the vault as a whole) at a tier.</summary>
    public static void ShowOffer(Window? owner, string? featureKey, int tier, Action signIn)
    {
        var feature = VaultOffer.Feature(featureKey);
        var dialog = new VaultGateDialog(feature, Math.Max(tier, feature?.Tier ?? 1), signIn) { Owner = owner };
        dialog._funnelKey = FunnelBeacon.Clean(featureKey) is { Length: > 0 } k ? k : "vault";
        dialog.BuildOffer();
        Present(dialog);
        FunnelBeacon.Post("gate_shown", dialog._funnelKey, FunnelBeacon.TierName(dialog._tier));
        _ = dialog.FillPlansAsync();
        _ = dialog.FillAsyncBits();
    }

    private static void Present(VaultGateDialog dialog)
    {
        try { _open?.Close(); }
        catch (InvalidOperationException) { } // swallow: the previous card was already closing
        _open = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(_open, dialog)) _open = null; };
        dialog.Show();
    }

    /// <summary>The last-day card of an invite week.</summary>
    public static void ShowEnding(Window? owner, DateTime grantUntilUtc, Action signIn)
    {
        var dialog = new VaultGateDialog(null, 1, signIn) { Owner = owner };
        dialog._funnelKey = "invite_ending";
        dialog.BuildEnding(grantUntilUtc);
        Present(dialog);
        FunnelBeacon.Post("gate_shown", dialog._funnelKey, "basic");
        _ = dialog.FillPlansAsync();
    }

    // ============================== offer ==============================

    private StackPanel? _proofSlot;
    private StackPanel? _codeSlot;

    private void BuildOffer()
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        var lab = _tier >= 2;
        _body.Children.Add(Chip(Loc.Get(lab ? "vaultgate_chip_lab" : "vaultgate_chip_vault"), lab ? Gold : null));

        if (_feature != null) _body.Children.Add(Art(_feature.ArtResource));

        var name = _feature == null ? null : Loc.Get(_feature.TitleLocKey);
        _body.Children.Add(Heading(name == null
            ? Loc.Get("vaultgate_title_generic")
            : Loc.GetF(lab ? "vaultgate_title_lab" : "vaultgate_title_vault", name)));
        _body.Children.Add(Para(_feature == null ? Loc.Get("vaultgate_generic_body") : Loc.Get(_feature.TaglineLocKey)));

        _body.Children.Add(Perks(lab, 12.5));

        _proofSlot = new StackPanel();
        _body.Children.Add(_proofSlot);
        PaintProof();

        _body.Children.Add(Price(_tier, hints: true));

        var open = PrimaryButton(Loc.Get(lab ? "vaultgate_open_lab" : "vaultgate_open_vault"));
        open.Click += (_, _) => { OpenPlan(_tier, "gate"); Close(); };
        _body.Children.Add(open);
        _body.Children.Add(CardLink(lab, "gate"));

        var links = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        links.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        links.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var signIn = Link(Loc.Get("vaultgate_signin"));
        signIn.MouseLeftButtonUp += (_, _) => { Close(); _signIn(); };
        links.Children.Add(signIn);
        var compare = Link(Loc.Get("vaultgate_compare"));
        compare.MouseLeftButtonUp += (_, _) => BuildCompare();
        Grid.SetColumn(compare, 1);
        links.Children.Add(compare);
        _body.Children.Add(links);

        _codeSlot = new StackPanel();
        _body.Children.Add(_codeSlot);

        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
        _body.Children.Add(CloseLink());
    }

    /// <summary>The two network bits the card never waits for: the supporter count and whether
    /// invites exist server-side. Each repaints its own slot when it lands.</summary>
    private async Task FillAsyncBits()
    {
        try
        {
            if (DateTime.UtcNow - _supportersReadUtc > TimeSpan.FromHours(6))
            {
                _supportersReadUtc = DateTime.UtcNow;
                using var res = await Http.GetAsync($"{BackRoomApi.BaseUrl}/v2/public/supporters");
                if (res.IsSuccessStatusCode)
                    _supporters = VaultOffer.ParseSupporterCount(await res.Content.ReadAsStringAsync());
                PaintProof();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] supporters read failed: {E}", ex.GetType().Name); }

        try
        {
            if (BackRoomApi.AppIdentity() == null) return;
            if (_invitesReachable == null || DateTime.UtcNow - _invitesReadUtc > TimeSpan.FromMinutes(10))
            {
                _invitesReadUtc = DateTime.UtcNow;
                _invitesReachable = (await new InviteApi().MineAsync()).Reachable;
            }
            if (_invitesReachable == true && _codeSlot != null && IsLoaded)
            {
                var link = Link(Loc.Get("vaultgate_have_code"));
                link.Margin = new Thickness(0, 8, 0, 0);
                link.MouseLeftButtonUp += (_, _) =>
                {
                    var box = new InviteRedeemBox("gate") { Margin = new Thickness(0, 10, 0, 0) };
                    _codeSlot.Children.Clear();
                    _codeSlot.Children.Add(box);
                    box.FocusCode();
                };
                _codeSlot.Children.Clear();
                _codeSlot.Children.Add(link);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] invites probe failed: {E}", ex.GetType().Name); }
    }

    /// <summary>Reads the plan list (cached ten minutes by <see cref="PlanCatalog"/>) and the sale,
    /// then repaints the prices. The sale rides on the plan list once the server sends one; until
    /// then (fallback list) the card reads /config/vault-sale as before. Any failure means the
    /// compiled prices and no sale.</summary>
    private async Task FillPlansAsync()
    {
        try
        {
            _plans = await PlanCatalog.GetAsync();
            if (_plans.FromServer)
            {
                _sale = _plans.Sale;
                _saleReadUtc = DateTime.UtcNow;
                PaintPrices();
                return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] plans read failed: {E}", ex.GetType().Name); }
        PaintPrices();
        await FillSaleAsync();
    }

    /// <summary>Reads the sale switch (cached five minutes) and repaints the prices when it lands.
    /// Any failure means no sale: the card falls back to the normal prices.</summary>
    private async Task FillSaleAsync()
    {
        if (DateTime.UtcNow - _saleReadUtc <= TimeSpan.FromMinutes(5)) return;
        _saleReadUtc = DateTime.UtcNow;
        VaultSaleInfo? sale = null;
        try
        {
            using var res = await Http.GetAsync($"{BackRoomApi.BaseUrl}/config/vault-sale");
            if (res.IsSuccessStatusCode) sale = VaultSale.Parse(await res.Content.ReadAsStringAsync());
        }
        catch (Exception ex) { App.Logger?.Debug("[VaultGate] sale read failed: {E}", ex.GetType().Name); }
        _sale = sale;
        PaintPrices();
    }

    private void PaintPrices()
    {
        foreach (var (slot, tier, compact, yearly, hints) in _priceSlots) PaintPrice(slot, tier, compact, yearly, hints);
        PaintStamps();
    }

    private void PaintProof()
    {
        if (_proofSlot == null) return;
        _proofSlot.Children.Clear();
        var floor = VaultOffer.SupporterFloor(_supporters);
        if (floor == null) return;
        _proofSlot.Children.Add(Para(Loc.GetF("vaultgate_proof", floor), Text, 12.5, new Thickness(0, 10, 0, 0)));
    }

    // ============================== ending ==============================

    private void BuildEnding(DateTime grantUntilUtc)
    {
        _body.Children.Clear();
        _priceSlots.Clear();
        _body.Children.Add(Chip(Loc.Get("vaultgate_ending_chip"), Warn));
        _body.Children.Add(Heading(Loc.Get("vaultgate_ending_title")));
        _body.Children.Add(new Border
        {
            Background = RowBg, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12, 12, 12, 4), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = Loc.Get("vaultgate_ending_bubble"), Foreground = Text, FontSize = 13, TextWrapping = TextWrapping.Wrap },
        });
        _body.Children.Add(Para(Loc.GetF("vaultgate_ending_until", grantUntilUtc.ToLocalTime().ToString("ddd d MMM, HH:mm")),
            Warn, 12.5, new Thickness(0, 10, 0, 0)));
        _body.Children.Add(Price(1, hints: true));

        var keep = PrimaryButton(Loc.Get("vaultgate_ending_keep"));
        keep.Click += (_, _) => { OpenPlan(1, "ending"); Close(); };
        _body.Children.Add(keep);
        _body.Children.Add(CardLink(false, "ending"));

        var let = Link(Loc.Get("vaultgate_ending_let_close"));
        let.HorizontalAlignment = HorizontalAlignment.Center;
        let.Margin = new Thickness(0, 12, 0, 0);
        let.MouseLeftButtonUp += (_, _) => Close();
        _body.Children.Add(let);
        _body.Children.Add(Fine(Loc.Get("vaultgate_fine")));
    }

    // ============================== bits ==============================

    /// <summary>The tier's Patreon checkout (the reward preselected), from the plan list.</summary>
    private void OpenPlan(int tier, string from)
    {
        var plan = _plans.For(tier);
        App.Logger?.Information("[VaultGate] open {Plan} checkout from {From}", plan.Key, from);
        FunnelBeacon.Post("cta_clicked", _funnelKey, "patreon");
        OpenUrl(plan.PatreonUrl);
    }

    private static void OpenUrl(string url)
    {
        Services.CheckoutWatch.NoteOpened(url);
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { App.Logger?.Error(ex, "[VaultGate] failed to open {Url}", url); }
    }

    /// <summary>The other ways to pay, under every Patreon button: the site's card / PayPal checkout
    /// and the Bitcoin pass page.</summary>
    private FrameworkElement CardLink(bool lab, string from) => PayWays(lab ? 2 : 1, from, Muted, Muted, stacked: false);

    /// <summary>"or pay with card or PayPal · Bitcoin": one row under a button, or stacked on a ticket stub.</summary>
    private FrameworkElement PayWays(int tier, string from, Brush lead, Brush ink, bool stacked)
    {
        Panel row = stacked ? new StackPanel() : new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(new TextBlock
        {
            Text = Loc.Get("vaultgate_pay_with"), Foreground = lead, FontSize = stacked ? 11.5 : 12,
            Margin = stacked ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 4, 0),
        });
        var card = Link(Loc.Get("vaultgate_or_card"));
        card.Foreground = ink;
        card.MouseLeftButtonUp += (_, _) => { OpenCard(tier, from); Close(); };
        row.Children.Add(card);
        if (!stacked) row.Children.Add(new TextBlock { Text = "·", Foreground = lead, FontSize = 12, Margin = new Thickness(6, 0, 6, 0) });
        var coin = Link(Loc.Get("vaultgate_or_crypto"));
        coin.Foreground = ink;
        coin.ToolTip = Loc.Get("vaultgate_crypto_tip");
        if (stacked) coin.Margin = new Thickness(0, 4, 0, 0);
        coin.MouseLeftButtonUp += (_, _) => { OpenCrypto(tier, from); Close(); };
        row.Children.Add(coin);
        return row;
    }

    /// <summary>The site's Bitcoin pass page: paid once, delivered as a code, never a subscription.
    /// The page reads no plan from its address, so the tier only goes to the log.</summary>
    private void OpenCrypto(int tier, string from)
    {
        App.Logger?.Information("[VaultGate] open crypto pass page ({Tier}) from {From}", FunnelBeacon.TierName(tier), from);
        FunnelBeacon.Post("cta_clicked", _funnelKey, "crypto");
        OpenUrl(PlanCatalog.CryptoPassUrl);
    }

    /// <summary>The site's own checkout (card or PayPal, monthly), tagged from=panel.</summary>
    private void OpenCard(int tier, string from)
    {
        var plan = _plans.For(tier);
        App.Logger?.Information("[VaultGate] open card checkout ({Plan}) from {From}", plan.Key, from);
        FunnelBeacon.Post("cta_clicked", _funnelKey, "web");
        OpenUrl(PlanCatalog.WebUrlFor(plan, "panel"));
    }

    private static FrameworkElement Chip(string text, Brush? tint)
    {
        var t = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold };
        var b = new Border
        {
            CornerRadius = new CornerRadius(99), Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Child = t,
        };
        if (tint != null)
        {
            t.Foreground = tint;
            b.Background = new SolidColorBrush(((SolidColorBrush)tint).Color) { Opacity = 0.16 };
        }
        else
        {
            t.SetResourceReference(TextBlock.ForegroundProperty, "PinkBrush");
            b.Background = Frozen("#24FF69B4");
        }
        return b;
    }

    private static FrameworkElement Art(string packPath)
    {
        var host = new Border { Height = 150, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true, Background = RowBg };
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/" + packPath.TrimStart('/'));
            img.DecodePixelWidth = 840;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            host.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) { Diag.Swallowed(ex, "gate art missing"); }
        return host;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
        FontWeight = FontWeights.SemiBold, FontSize = 21, Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
    };

    private static TextBlock Para(string text, Brush? brush = null, double size = 13, Thickness? margin = null) => new()
    {
        Text = text, Foreground = brush ?? Muted, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 4, 0, 0),
    };

    /// <summary>What a tier adds, as the gate audit found it (2026-10-02): Basic over free, Prime
    /// over Basic. Keep these lines true when a gate moves; the card is the only place a free
    /// account reads the full list.</summary>
    private static readonly string[] BasicPerks =
    {
        // vaultgate_basic_4 ("Haptics synced to your toys") left 2026-11-11: toys are free now.
        "vaultgate_basic_1", "vaultgate_basic_10", "vaultgate_basic_2", "vaultgate_basic_3",
        "vaultgate_basic_5", "vaultgate_basic_6", "vaultgate_basic_7", "vaultgate_basic_8",
        "vaultgate_basic_9",
    };

    private static readonly string[] PrimePerks =
    {
        "vaultgate_prime_8", "vaultgate_prime_1", "vaultgate_prime_3", "vaultgate_prime_4",
        "vaultgate_prime_6", "vaultgate_prime_7",
    };

    /// <summary>The tier's header line and its bullets: a small dot per row, tight spacing, so
    /// eight lines cost the card little height.</summary>
    private static FrameworkElement Perks(bool lab, double size)
    {
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        list.Children.Add(new TextBlock
        {
            Text = Loc.Get(lab ? "vaultgate_prime_head" : "vaultgate_basic_head"),
            Foreground = Muted, FontSize = size - 0.5, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (var key in lab ? PrimePerks : BasicPerks)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 5, Height = 5, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, size * 0.47, 0, 0),
            };
            if (lab) dot.Fill = Gold; else dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PinkBrush");
            row.Children.Add(dot);
            var line = new TextBlock { Text = Loc.Get(key), Foreground = Text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>A price slot. <paramref name="hints"/> adds the euro line and the yearly line
    /// under a monthly price (the offer and ending faces); the compare columns leave them out.</summary>
    private FrameworkElement Price(int tier, bool compact = false, bool yearly = false, bool hints = false)
    {
        var slot = new StackPanel { Margin = new Thickness(0, compact ? 8 : 12, 0, compact ? 0 : 4) };
        _priceSlots.Add((slot, tier, compact, yearly, hints));
        PaintPrice(slot, tier, compact, yearly, hints);
        return slot;
    }

    /// <summary>The price, or during a first-month sale the sale price beside the struck normal
    /// one, the "then" line and the end date. Yearly never shows a sale. With hints, a normal
    /// monthly price also gets the euro line and the yearly line.</summary>
    private void PaintPrice(StackPanel slot, int tier, bool compact, bool yearly, bool hints = false)
    {
        slot.Children.Clear();
        var price = VaultOffer.PriceFor(tier, _currency, _plans);
        var sale = VaultSale.AppliesTo(_sale, tier, DateTime.UtcNow, yearly) ? _sale : null;
        var line = new WrapPanel();
        line.Children.Add(new TextBlock
        {
            Text = VaultOffer.Money(sale != null ? VaultSale.FirstMonthCents(price.MonthlyCents, sale.Percent)
                : yearly ? price.YearlyCents : price.MonthlyCents, _currency),
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
            FontWeight = FontWeights.SemiBold, FontSize = compact ? 22 : 28, Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 8, 0),
        });
        if (sale != null)
            line.Children.Add(new TextBlock
            {
                Text = VaultOffer.Money(price.MonthlyCents, _currency),
                TextDecorations = TextDecorations.Strikethrough,
                FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
                Foreground = Dim, FontSize = compact ? 14 : 17, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, compact ? 3 : 4),
            });
        line.Children.Add(new TextBlock
        {
            Text = sale != null
                ? Loc.Get("vaultgate_sale_first")
                : yearly
                    ? Loc.GetF("vaultgate_price_year", VaultOffer.YearlyPerMonth(price))
                    : Loc.GetF("vaultgate_price", VaultOffer.PerDay(price)),
            Foreground = Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5),
            TextWrapping = TextWrapping.Wrap,
        });
        slot.Children.Add(line);
        if (sale == null)
        {
            if (hints && !yearly) AddHints(slot, tier, price);
            return;
        }

        slot.Children.Add(Para(Loc.GetF("vaultgate_sale_line", sale.Percent, VaultOffer.Money(price.MonthlyCents, _currency)),
            Good, compact ? 12 : 12.5, new Thickness(0, 2, 0, 0)));
        if (VaultSale.EndsText(sale) is string ends)
            slot.Children.Add(Para(Loc.GetF("vaultgate_sale_ends", ends), Dim, compact ? 11.5 : 12, new Thickness(0, 2, 0, 0)));
    }

    /// <summary>Under a normal monthly price: "or €6 a month in euros" when the headline is in
    /// dollars, then "Or $72 a year on Patreon: 20% off."</summary>
    private void AddHints(StackPanel slot, int tier, TierPrice price)
    {
        if (_currency == PriceCurrency.Usd)
        {
            var eur = VaultOffer.PriceFor(tier, PriceCurrency.Eur, _plans);
            slot.Children.Add(Para(Loc.GetF("vaultgate_price_eur", VaultOffer.Money(eur.MonthlyCents, PriceCurrency.Eur)),
                Dim, 12, new Thickness(0, 0, 0, 0)));
        }
        var off = PlanCatalog.YearlyOffPercent(_plans.For(tier));
        if (off > 0)
            slot.Children.Add(Para(Loc.GetF("vaultgate_yearly_hint", VaultOffer.Money(price.YearlyCents, _currency), off),
                Gold, 12, new Thickness(0, 2, 0, 6)));
    }

    private static Button PrimaryButton(string text)
    {
        var b = SmallButton(text);
        b.FontSize = 15;
        b.Padding = new Thickness(14, 10, 14, 10);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    private static Button SmallButton(string text)
    {
        var b = new Button { Content = text, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(StyleProperty, "SmallPinkButton");
        return b;
    }

    /// <summary>True when the press landed on (or inside) something that wears the hand cursor.</summary>
    private static bool OnClickable(DependencyObject? d)
    {
        for (; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (d is Window) return false;
        }
        return false;
    }

    private static TextBlock Link(string text) => new()
    {
        Text = text, Foreground = Muted, FontSize = 12, Cursor = Cursors.Hand,
        TextDecorations = TextDecorations.Underline, TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Fine(string text) => new()
    {
        Text = text, Foreground = Dim, FontSize = 11, TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0),
    };

    private FrameworkElement CloseLink()
    {
        var l = Link(Loc.Get("vaultgate_close"));
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.Margin = new Thickness(0, 8, 0, 0);
        l.MouseLeftButtonUp += (_, _) => Close();
        return l;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
