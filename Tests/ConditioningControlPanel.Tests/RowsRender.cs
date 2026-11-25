using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep helper (overnight sweep 2026-11-24), generalised from the Flash pilot's
/// render test. Call it from inside <see cref="WpfRenderHarness.OnStaThread"/> in a class marked
/// <c>[Collection(CompanionWpfRenderCollection.Name)]</c>:
/// <code>RowsRender.Check(new VideoFeatureControl(), "video", show: new[] { "RowLightsDown" });</code>
/// That hosts the control at 900 px, opens every fold, shows the named elements, lays it out,
/// asserts the rows rules on every <see cref="RowLabel"/> and, with env <c>CCP_ROWS_DUMP=&lt;dir&gt;</c>,
/// saves <c>&lt;dir&gt;/&lt;name&gt;.png</c> at 96 dpi. The render half alone (BEFORE stills, pages with
/// no RowLabel yet) is <see cref="Host"/> + <see cref="Dump"/>.
/// </summary>
internal static class RowsRender
{
    public const double Width = 900;

    /// <summary>The panel's dark page colour behind every still.</summary>
    public static readonly Color PageBack = Color.FromRgb(0x16, 0x13, 0x22);

    /// <summary>
    /// The one call: host, open, show, lay out, assert, dump. Returns the RowLabels found so a test
    /// can go on to check values and IsOn. <paramref name="minLabels"/> guards against a page whose
    /// rows silently did not convert. Labels whose text is in <paramref name="noEmblem"/> may go
    /// without a picture (the map's "none" rows that still took RowLabel).
    /// </summary>
    public static IReadOnlyList<RowLabel> Check(FrameworkElement page, string name, int minLabels = 1,
        IEnumerable<string>? show = null, IEnumerable<string>? noEmblem = null, double width = Width,
        Action<FrameworkElement>? before = null, string? lang = null)
    {
        // lang (or env CCP_ROWS_LANG) re-reads the page's {loc:Str} bindings in that language for the
        // check and the still, then puts the previous language back.
        var labels = InLanguage(lang, () =>
        {
            var host = Host(page, width, show, before);
            var found = AssertRows(page, minLabels, noEmblem);
            Dump(host, name);
            return found;
        });
        AssertSilent(name);
        return labels;
    }

    /// <summary>
    /// Puts <paramref name="page"/> in a dark host <paramref name="width"/> wide, opens every
    /// <see cref="OptionSection"/>, makes each element named in <paramref name="show"/> Visible
    /// (FindName on the page; a missing name fails loudly), runs <paramref name="before"/> (set
    /// values, switches, SuperRows.Apply...) and lays the host out. Returns the host.
    /// </summary>
    public static Grid Host(FrameworkElement page, double width = Width, IEnumerable<string>? show = null,
        Action<FrameworkElement>? before = null)
    {
        var host = new Grid { Width = width, Background = new SolidColorBrush(PageBack) };
        host.Children.Add(page);
        foreach (var s in Descendants(page).OfType<OptionSection>()) s.IsOpen = true;
        foreach (var n in show ?? Array.Empty<string>())
        {
            var el = page.FindName(n) as FrameworkElement;
            Assert.True(el != null, $"show: no element named '{n}' on {page.GetType().Name}");
            el!.Visibility = Visibility.Visible;
        }
        before?.Invoke(page);
        Layout(host, width);
        // Opening folds and showing rows can realise new sections; open those too and lay out again.
        bool more = false;
        foreach (var s in Descendants(page).OfType<OptionSection>())
            if (!s.IsOpen) { s.IsOpen = true; more = true; }
        if (more) Layout(host, width);
        PlayPreviews(page);
        return host;
    }

    /// <summary>
    /// A page whose side card holds the live preview: plays two seconds of it by hand (an element that
    /// is never loaded never opens its driver), so a still shows the scene and not an empty desktop.
    /// </summary>
    public static void PlayPreviews(FrameworkElement page)
    {
        foreach (var pv in Descendants(page).OfType<ConditioningControlPanel.Controls.Preview.EffectPreview>())
        {
            if (pv.Driver == null || pv.ActualHeight < 1) continue;
            pv.Driver.Open();
            for (int i = 0; i < 60; i++) pv.Driver.Scene.Tick(1 / 30.0);
            pv.Repaint();
        }
    }

    /// <summary>
    /// The rows rules on every visible RowLabel under <paramref name="root"/>: an emblem (unless the
    /// label's text is in <paramref name="noEmblem"/>), an OptionRow parent Grid (MinHeight 46, no
    /// fixed Height) tall enough for the label, nothing in the row starting left of the label's right
    /// edge, the text untrimmed and no leading emoji left when it has an emblem.
    /// </summary>
    public static IReadOnlyList<RowLabel> AssertRows(FrameworkElement root, int minLabels = 1, IEnumerable<string>? noEmblem = null)
    {
        var labels = Descendants(root).OfType<RowLabel>().Where(IsShown).ToList();
        Assert.True(labels.Count >= minLabels, $"only {labels.Count} RowLabels on {root.GetType().Name} (want {minLabels})");
        var problems = Problems(labels, noEmblem);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        return labels;
    }

    /// <summary>
    /// The rows rules of <see cref="AssertRows"/> as a list of sentences instead of a first failure,
    /// for the language sweep (which reports what squeezes rather than failing on it).
    /// </summary>
    public static List<string> Problems(IEnumerable<RowLabel> labels, IEnumerable<string>? noEmblem = null, bool squeeze = false)
    {
        var bare = new HashSet<string>(noEmblem ?? Array.Empty<string>(), StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var label in labels)
        {
            if (!bare.Contains(label.Text ?? "") && !bare.Contains(label.ShownText) && !label.Emblem.HasValue)
                problems.Add($"'{label.Text}' has no emblem");
            if (label.Parent is Grid row)
            {
                if (row.MinHeight < 46) problems.Add($"'{label.ShownText}' row is not an OptionRow");
                if (!double.IsNaN(row.Height)) problems.Add($"'{label.ShownText}' row keeps a fixed Height");
                if (row.ActualHeight + 0.5 < label.DesiredSize.Height) problems.Add($"'{label.ShownText}' is clipped by its row");
                double right = label.TranslatePoint(new Point(label.ActualWidth, 0), row).X;
                if (right > row.ActualWidth + 0.5)
                    problems.Add($"'{label.ShownText}' runs past its row ({right:0} > {row.ActualWidth:0})");
                foreach (UIElement other in row.Children)
                {
                    if (other == label || other is not FrameworkElement fe || fe.Visibility != Visibility.Visible) continue;
                    // Only siblings sharing the label's line can overlap it.
                    if (Grid.GetRow(fe) != Grid.GetRow(label)) continue;
                    double left = fe.TranslatePoint(new Point(0, 0), row).X;
                    if (left + 0.5 < right) problems.Add($"'{label.ShownText}' overlaps {fe.GetType().Name} {fe.Name}");
                    else if (squeeze && fe.ActualWidth < 1 && fe.DesiredSize.Width > 1)
                        problems.Add($"'{label.ShownText}' squeezes {fe.GetType().Name} {fe.Name} to nothing");
                }
            }
            var tb = label.TextPart;
            if (string.IsNullOrEmpty(tb.Text)) continue;
            if (Clipped(label) is { } clip) problems.Add(clip);
            if (label.Emblem.HasValue && LeadingEmoji.Strip(tb.Text) != tb.Text)
                problems.Add($"'{tb.Text}' still leads with an emoji");
        }
        return problems;
    }

    /// <summary>Most lines a label may wrap to before the page counts as squeezed.</summary>
    public const int MaxLabelLines = 3;

    /// <summary>
    /// Null when the label's text is drawn whole: never trimmed, no word wider than its box, every
    /// wrapped line inside the box's height, at most <see cref="MaxLabelLines"/> lines. Wrapping is
    /// allowed (integration 2026-11-24). Otherwise a sentence naming what is wrong.
    /// </summary>
    public static string? Clipped(RowLabel label)
    {
        var tb = label.TextPart;
        if (string.IsNullOrEmpty(tb.Text)) return null;
        if (tb.TextTrimming != TextTrimming.None) return $"'{tb.Text}' can be trimmed ({tb.TextTrimming})";
        var face = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        FormattedText Fmt(string s) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, tb.FontSize, Brushes.White, 1.0);
        double box = tb.ActualWidth;
        foreach (var word in tb.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            double w = Fmt(word).Width;
            if (w > box + 1) return $"'{tb.Text}': the word '{word}' is wider than its box ({w:0} > {box:0})";
        }
        var all = Fmt(tb.Text);
        if (tb.TextWrapping == TextWrapping.NoWrap)
            return all.WidthIncludingTrailingWhitespace <= box + 1 ? null
                : $"'{tb.Text}' is trimmed ({all.WidthIncludingTrailingWhitespace:0} > {box:0})";
        // +1: layout rounding can hand the TextBlock a hair less than the text's own width.
        all.MaxTextWidth = Math.Max(1, box + 1);
        if (all.Height > tb.ActualHeight + 1) return $"'{tb.Text}' is cut off ({all.Height:0} > {tb.ActualHeight:0} px tall)";
        int lines = Lines(label);
        if (lines > MaxLabelLines) return $"'{tb.Text}' wraps to {lines} lines (box {box:0} px)";
        return null;
    }

    /// <summary>How many lines the label's text is drawn on.</summary>
    public static int Lines(RowLabel label)
    {
        var tb = label.TextPart;
        if (string.IsNullOrEmpty(tb.Text)) return 0;
        var one = new FormattedText("Ag", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.White, 1.0);
        return Math.Max(1, (int)Math.Round(tb.ActualHeight / Math.Max(1, one.Height)));
    }

    /// <summary>
    /// Runs <paramref name="body"/> with the UI in <paramref name="lang"/> (a code of
    /// <c>LocalizationManager.AvailableLanguages</c>; null or empty = env <c>CCP_ROWS_LANG</c>, else
    /// unchanged) and puts the previous language back afterwards, so other tests keep English.
    /// Build the page INSIDE the body: <c>{loc:Str}</c> bindings re-read on the switch, but text set
    /// from code at construction does not.
    /// </summary>
    public static T InLanguage<T>(string? lang, Func<T> body)
    {
        lang = string.IsNullOrWhiteSpace(lang) ? Environment.GetEnvironmentVariable("CCP_ROWS_LANG") : lang;
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        if (string.IsNullOrWhiteSpace(lang) || lang == loc.CurrentLanguage) return body();
        string was = loc.CurrentLanguage;
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            loc.SetLanguage(lang);
            return body();
        }
        finally
        {
            loc.SetLanguage(was);
            CultureInfo.CurrentUICulture = culture;
        }
    }

    /// <summary>
    /// Saves <paramref name="host"/> as <c>&lt;dir&gt;/&lt;name&gt;.png</c> at <paramref name="dpi"/>;
    /// <paramref name="dir"/> defaults to env CCP_ROWS_DUMP. Returns the path, or null when no folder
    /// was asked for.
    /// </summary>
    public static string? Dump(FrameworkElement host, string name, double dpi = 96, string? dir = null)
    {
        dir ??= Environment.GetEnvironmentVariable("CCP_ROWS_DUMP");
        if (string.IsNullOrWhiteSpace(dir)) return null;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        int w = Math.Max(1, (int)Math.Ceiling(host.ActualWidth * dpi / 96));
        int h = Math.Max(1, (int)Math.Ceiling(host.ActualHeight * dpi / 96));
        var bmp = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }

    /// <summary>
    /// Render tests must be SILENT (owner at the PC, 2026-11-24: a stills run played the Dollhouse
    /// menu song). A WPF MediaPlayer / MediaElement loads wmp.dll into the test process the moment
    /// it opens media; fail loudly when it is there, naming what was rendered last.
    /// </summary>
    public static void AssertSilent(string what)
    {
        foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
            Assert.False(string.Equals(m.ModuleName, "wmp.dll", StringComparison.OrdinalIgnoreCase),
                $"wmp.dll is loaded after rendering '{what}': a page started a media player. Leave it out of render tests.");
    }

    public static void Layout(FrameworkElement host, double width = Width)
    {
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        // ElementName bindings (IsOn, RowFraction) can be left Unattached until the dispatcher runs
        // its DataBind work: seen when another big page (SettingsTabView) was built earlier in the
        // same process. Pump once below DataBind, then lay out again.
        host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
    }

    /// <summary>Logical and visual children both (a SuperBox hosts its rows in a template), each once.</summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var kids = LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().ToList();
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) kids.Add(VisualTreeHelper.GetChild(node, i));
            foreach (var k in kids)
                if (seen.Add(k)) { yield return k; stack.Push(k); }
        }
    }

    /// <summary>Visible on screen: the label and every ancestor up the visual tree are Visible.</summary>
    private static bool IsShown(RowLabel label) => label.IsVisible || IsVisibleUp(label);

    private static bool IsVisibleUp(DependencyObject d)
    {
        for (var n = d; n != null; n = VisualTreeHelper.GetParent(n) ?? LogicalTreeHelper.GetParent(n))
            if (n is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }
}
