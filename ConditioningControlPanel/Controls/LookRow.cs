using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Prizes;
using SuperCore = ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// A Studio "Look" row (owner 2026-11-17: "adding a customization option to each of those tabs in the studio is
/// owed"): the looks one effect can wear, as chips in the shop's order. Owned chips wear on click (the worn one
/// pressed in, with a tick); unowned ones stay VISIBLE but locked (dimmed, the star and price) and open the shop
/// on that item. Not-out-yet looks are not shown. Code-only on purpose (a UserControl namescope refuses
/// <c>x:Name</c> on the host's rows). Talks only to <see cref="LookWardrobe"/>.
/// </summary>
public class LookRow : StackPanel
{
    /// <summary>At most this many chips are built; the panel shows two lines of them and "More in the shop" for the rest.</summary>
    public const int MaxChips = 10;

    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(nameof(Slot), typeof(string), typeof(LookRow),
        new PropertyMetadata(null, (d, _) => ((LookRow)d).Rebuild()));

    /// <summary>The wardrobe slot: bubble, card, behind, word.</summary>
    public string? Slot { get => (string?)GetValue(SlotProperty); set => SetValue(SlotProperty, value); }

    /// <summary>A door for the shop on one item (tests swap it). Default: the counter opens on that item.</summary>
    internal static Action<string> OpenShopOn = id => Services.PrizeCounter.PrizeCounterHostService.LaunchOn(id);

    private readonly TwoLinePanel _chips = new();
    private readonly TextBlock _label;

    public LookRow()
    {
        Margin = new Thickness(0, 4, 0, 2);
        _label = LookChips.Label();
        Children.Add(_label);
        Children.Add(_chips);
        Loaded += (_, _) => { LookWardrobe.Changed += OnChanged; Rebuild(); };
        Unloaded += (_, _) => LookWardrobe.Changed -= OnChanged;
    }

    private void OnChanged(string slot) { if (slot == Slot) Rebuild(); }

    /// <summary>The chips again from the wardrobe (a wear here, in the shop, or a new language).</summary>
    public void Rebuild()
    {
        var slot = Slot;
        _chips.Children.Clear();
        if (string.IsNullOrEmpty(slot)) return;
        BindingOperations.SetBinding(_label, TextBlock.TextProperty, new Binding($"[shop_sheet_sec_{slot}]") { Source = LocalizationManager.Instance });
        AutomationProperties.SetName(_chips, Loc.Get("shop_sheet_sec_" + slot));
        var all = LookWardrobe.Choices(slot);
        var shown = all.Count > MaxChips ? all.Take(MaxChips).ToList() : all.ToList();
        foreach (var c in shown) _chips.Children.Add(Chip(c));
        // the last child: "More in the shop", shown by the panel only when the chips would need a third line
        var firstLocked = all.FirstOrDefault(c => !c.Owned) ?? all[^1];
        _chips.Children.Add(LookChips.Make(new ShopTag(), Loc.Get("studio_look_more"), LookChips.State.Locked, null, Loc.Get("studio_look_more"), () => OpenShopOn(firstLocked.Id)));
    }

    private UIElement Chip(LookChoice c)
    {
        var name = Loc.Get(c.NameKey);
        var tip = Loc.Get(c.TipKey);
        var state = c.Worn ? LookChips.State.Worn : c.Owned ? LookChips.State.Owned : LookChips.State.Locked;
        string auto = state switch
        {
            LookChips.State.Worn => Loc.GetF("studio_look_worn", name),
            LookChips.State.Locked => Loc.GetF("studio_look_locked", name, c.Price),
            _ => name,
        };
        if (state == LookChips.State.Locked) tip = tip + "\n" + Loc.GetF("studio_look_locked", name, c.Price);
        return LookChips.Make(Picture(c), name, state, state == LookChips.State.Locked ? c.Price : null, auto, () =>
        {
            if (c.Owned) LookWardrobe.Wear(c.Slot, c.Id);
            else OpenShopOn(c.Id);
        }, tip);
    }

    /// <summary>The look's small picture: a bubble still, a card band, "Aa" in the word face.</summary>
    private static UIElement? Picture(LookChoice c)
    {
        switch (c.Slot)
        {
            case WornLooks.Bubble:
                var img = LookThumbs.Bubble(WornLooks.SkinOf(c.Id));
                return img == null ? null : new Image { Source = img, Width = 22, Height = 22, SnapsToDevicePixels = true };
            case WornLooks.Card:
                var back = SuperCore.DeckLooks.BackOf(c.Id);
                if (back == SuperCore.DeckBack.Plain) return LookThumbs.PlainCard(16, 22);   // "no frame", never a pink card
                return new Border
                {
                    Width = 16, Height = 22, CornerRadius = new CornerRadius(3),
                    Background = LookThumbs.CardGround(back),
                    BorderBrush = LookThumbs.CardMark(back),
                    BorderThickness = new Thickness(3),
                };
            case WornLooks.Word:
                var style = global::ConditioningControlPanel.Looks.WordStyles.Of(c.Id);
                var aa = new TextBlock { Text = "Aa", FontSize = 14, FontWeight = FontWeights.Bold, FontFamily = LookThumbs.WordFont(style), VerticalAlignment = VerticalAlignment.Center };
                if (LookThumbs.WordInk(style) is { } ink) aa.Foreground = ink; else aa.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
                return aa;
            default:
                return null;
        }
    }
}

/// <summary>
/// Two-tone fog in the app (owner 2026-11-17: "we got no way to pick the second color once unlocked"): beside the
/// pink filter's own colour. Owned: a "Two-tone" switch and, while it is on, the palette swatches (the pick ringed,
/// the shop's line when the pick sits too close to the user's colour). Not owned: one locked chip that opens the shop
/// on it. Code-only, talks only to <see cref="LookWardrobe"/>.
/// </summary>
public class FogTwoToneBlock : StackPanel
{
    /// <summary>The user's own filter colour, asked at build time (the control's host knows it).</summary>
    public Func<(byte R, byte G, byte B)> OwnColour { get; set; } = () => (255, 105, 180);

    public FogTwoToneBlock()
    {
        Margin = new Thickness(0, 2, 0, 4);
        Loaded += (_, _) => { LookWardrobe.Changed += OnChanged; Rebuild(); };
        Unloaded += (_, _) => LookWardrobe.Changed -= OnChanged;
    }

    private void OnChanged(string slot) { if (slot is WornLooks.Mix or WornLooks.Fog2 or WornLooks.Fog) Rebuild(); }

    public void Rebuild()
    {
        Children.Clear();
        if (!LookWardrobe.TwoToneOwned)
        {
            var label = LookChips.Label();
            BindingOperations.SetBinding(label, TextBlock.TextProperty, new Binding("[shop_sheet_sec_fog]") { Source = LocalizationManager.Instance });
            var name = Loc.Get("shop_sheet_sec_fog");
            var price = LookWardrobe.PriceOf(SuperCore.FogLooks.Twotone);
            var wrap = new WrapPanel();
            wrap.Children.Add(LookChips.Make(null, Loc.Get("shop_item_fx_fog_twotone_name"), LookChips.State.Locked, price, Loc.GetF("studio_look_locked", name, price),
                () => LookRow.OpenShopOn(SuperCore.FogLooks.Twotone), Loc.Get("shop_sheet_tip_fog") + "\n" + Loc.GetF("studio_look_locked", name, price)));
            Children.Add(label);
            Children.Add(wrap);
            return;
        }
        // the switch, as every other switch on the page (a ToggleStyle CheckBox on the right)
        var grid = new Grid { Height = 34, Background = Brushes.Transparent, ToolTip = Loc.Get("shop_sheet_tip_fog") };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = LookChips.Label();
        title.Margin = new Thickness(0);
        BindingOperations.SetBinding(title, TextBlock.TextProperty, new Binding("[shop_item_fx_fog_twotone_name]") { Source = LocalizationManager.Instance });
        var on = LookWardrobe.TwoToneOn;
        var toggle = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "ToggleStyle");
        AutomationProperties.SetName(toggle, Loc.Get("shop_item_fx_fog_twotone_name"));
        toggle.Checked += (_, _) => LookWardrobe.TwoToneOn = true;
        toggle.Unchecked += (_, _) => LookWardrobe.TwoToneOn = false;
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(title);
        grid.Children.Add(toggle);
        Children.Add(grid);
        if (!on) return;

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var second = LookChips.Label();
        second.Margin = new Thickness(0);
        second.VerticalAlignment = VerticalAlignment.Center;
        second.Text = Loc.Get("shop_sheet_second_colour");
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        AutomationProperties.SetName(swatches, Loc.Get("shop_sheet_second_colour"));
        var picked = LookWardrobe.SecondFogColour;
        foreach (var (id, rgb) in SuperCore.FogLooks.Palette)
            swatches.Children.Add(LookChips.Swatch(Loc.Get(NameKey(id)), Color.FromRgb(rgb.R, rgb.G, rgb.B), id == picked, () => LookWardrobe.SecondFogColour = id));
        Grid.SetColumn(swatches, 1);
        row.Children.Add(second);
        row.Children.Add(swatches);
        Children.Add(row);

        var shown = SuperCore.FogLooks.SecondRow(picked, OwnColour());
        if (shown != picked)
        {
            var note = new TextBlock { Text = Loc.GetF("shop_sheet_fog2_close", Loc.Get(NameKey(picked)), Loc.Get(NameKey(shown))), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            Children.Add(note);
        }
    }

    private static string NameKey(string fogRow) => "shop_item_" + fogRow.Replace('.', '_') + "_name";
}

/// <summary>
/// Chips in wrapped lines, two at most: when they would need a third, the chips that do not fit give way to the
/// panel's LAST child ("More in the shop"), which is otherwise not shown. Hidden children are arranged to nothing.
/// </summary>
public sealed class TwoLinePanel : Panel
{
    public const int MaxLines = 2;
    private int _shown;   // children shown before the last one; -1 = all of them, no "more"

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        int n = InternalChildren.Count;
        foreach (UIElement c in InternalChildren) c.Measure(new Size(width, double.PositiveInfinity));
        if (n == 0) return new Size();
        var (lines, w, h) = Flow(n - 1, null, width);
        _shown = -1;
        if (lines <= MaxLines) return new Size(Math.Min(w, width), h);
        var more = InternalChildren[n - 1].DesiredSize;
        int k = n - 1;
        while (k > 0 && Flow(k, more, width).Lines > MaxLines) k--;
        _shown = k;
        var fit = Flow(k, more, width);
        return new Size(Math.Min(fit.Width, width), fit.Height);
    }

    /// <summary>Lines, width and height for the first <paramref name="count"/> children plus, if given, the "more" chip.</summary>
    private (int Lines, double Width, double Height) Flow(int count, Size? more, double width)
    {
        int lines = 1; double x = 0, lineH = 0, h = 0, w = 0;
        void Put(Size s)
        {
            if (x > 0 && x + s.Width > width) { lines++; h += lineH; x = 0; lineH = 0; }
            x += s.Width; lineH = Math.Max(lineH, s.Height); w = Math.Max(w, x);
        }
        for (int i = 0; i < count; i++) Put(InternalChildren[i].DesiredSize);
        if (more is { } m) Put(m);
        return (lines, w, h + lineH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int n = InternalChildren.Count;
        double x = 0, y = 0, lineH = 0;
        for (int i = 0; i < n; i++)
        {
            var c = InternalChildren[i];
            bool isMore = i == n - 1;
            bool show = _shown < 0 ? !isMore : (i < _shown || isMore);
            if (!show) { c.Arrange(new Rect(0, 0, 0, 0)); KeyboardNavigation.SetIsTabStop(c, false); continue; }
            KeyboardNavigation.SetIsTabStop(c, true);
            var s = c.DesiredSize;
            if (x > 0 && x + s.Width > final.Width) { y += lineH; x = 0; lineH = 0; }
            c.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width; lineH = Math.Max(lineH, s.Height);
        }
        return final;
    }

    /// <summary>Whether a child is on screen (tests, automation).</summary>
    public bool Shows(UIElement child)
    {
        int i = InternalChildren.IndexOf(child), n = InternalChildren.Count;
        return i >= 0 && (_shown < 0 ? i < n - 1 : (i < _shown || i == n - 1));
    }
}

/// <summary>The chip and swatch pieces the look rows share (Depth law: owned chips sit up, the worn one is pressed in).</summary>
internal static class LookChips
{
    public enum State { Owned, Worn, Locked }

    public static TextBlock Label()
    {
        var t = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5), VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextLightBrush");
        return t;
    }

    private static ControlTemplate? s_bare;

    /// <summary>A button that draws only its content (the chip border is the content), keyboard focus kept.</summary>
    private static ControlTemplate Bare()
    {
        if (s_bare != null) return s_bare;
        var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        t.Seal();
        return s_bare = t;
    }

    public static Button Make(UIElement? picture, string name, State state, int? price, string automation, Action click, string? tip = null)
    {
        var face = new Border
        {
            CornerRadius = new CornerRadius(9), Padding = new Thickness(picture == null ? 8 : 4, 2, 8, 2), MinHeight = 30,
            BorderThickness = new Thickness(state == State.Worn ? 1.5 : 1),
        };
        // ON IS PRESSED IN: the worn chip sits in the well with the section hue round it; owned ones stand up.
        face.SetResourceReference(Border.BackgroundProperty, state == State.Worn ? "DepthWellFloorBrush" : "DepthRaisedSheen");
        face.SetResourceReference(Border.BorderBrushProperty, state == State.Worn ? "PinkBrush" : "DepthRaisedBevel");
        var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (picture is FrameworkElement pic) { pic.Margin = new Thickness(0, 0, 6, 0); pic.VerticalAlignment = VerticalAlignment.Center; inner.Children.Add(pic); }
        var text = new TextBlock { Text = name, FontSize = 12, FontWeight = state == State.Worn ? FontWeights.Bold : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, state == State.Locked ? "TextMutedBrush" : "TextLightBrush");
        inner.Children.Add(text);
        if (state == State.Worn)
        {
            var tick = new TextBlock { Text = "✓", FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI") };
            tick.SetResourceReference(TextBlock.ForegroundProperty, "PinkBrush");
            inner.Children.Add(tick);
        }
        if (state == State.Locked)
        {
            if (picture != null && picture is not ShopTag) picture.Opacity = 0.45;   // the gold SHOP door never dims
            if (price is int p)
            {
                inner.Children.Add(new SparkleStar { Width = 12, Height = 12, Margin = new Thickness(5, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
                var pt = new TextBlock { Text = p.ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 11.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                pt.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                inner.Children.Add(pt);
            }
        }
        face.Child = inner;
        var b = new Button { Content = face, Template = Bare(), Margin = new Thickness(0, 0, 5, 5), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip, Tag = state };
        AutomationProperties.SetName(b, automation);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>One round fog swatch; the picked one ringed in the section hue.</summary>
    public static Button Swatch(string name, Color colour, bool picked, Action click)
    {
        var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(colour), BorderThickness = new Thickness(1) };
        dot.SetResourceReference(Border.BorderBrushProperty, "DepthRaisedBevel");
        var ring = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(picked ? 2 : 0), Child = dot, Background = Brushes.Transparent };
        ring.SetResourceReference(Border.BorderBrushProperty, "PinkBrush");
        var b = new Button { Content = ring, Template = Bare(), Margin = new Thickness(2, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = name, Tag = picked };
        AutomationProperties.SetName(b, picked ? Loc.GetF("studio_look_worn", name) : name);
        b.Click += (_, _) => click();
        return b;
    }
}
