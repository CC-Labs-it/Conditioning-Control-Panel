using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 5): the ComboBox on a rows-look option row showed its value in
/// the compact 11 px DarkComboBoxStyle face beside 16 px bold labels ("Mix", "Full"). Every combo on
/// a rows row (inside an OptionRow grid, or beside a RowLabel) now wears RowComboBoxStyle: 14 Bold
/// Nunito in TextLight. Combos elsewhere keep their look.
/// <para>Wave 6 (reviewer): the bigger face cut values in boxes that kept a fixed Width ("Default
/// (follow multi-monitor)" on three effect pages, German ramp curves, Russian monitor lists). The rule
/// (<see cref="RowComboFit"/>) sizes each box to its widest item, at most 60% of its row; these tests
/// measure every such box with the items the CODE fills in (monitor lists, mercy counts, devices) in all
/// ten languages, at the page's usual width and at the narrowest one it really gets.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowComboTests
{
    private static readonly string[] PageNames =
    {
        "flash", "video", "visuals", "lockcard", "wallpaper", "bubbles", "bubblecount", "bouncingtext", "subliminal",
        "spiral", "pinkfilter", "braindrain", "mindwipe", "scheduler", "ramp",
        "settings-audio", "settings-devices", "settings-emidesk", "settings-general", "settings-notifications",
        "settings-performance", "haptics", "lockdown", "awareness", "companion-picker",
    };

    public static IEnumerable<object[]> Pages() => PageNames.Select(p => new object[] { p });

    private static FrameworkElement Make(string page) => page switch
    {
        "flash" => new FlashFeatureControl(),
        "companion-picker" => new ConditioningControlPanel.Views.Controls.Companion.CompanionPickerCard(),
        _ => RowsPageStillsTests.Make(page),
    };

    /// <summary>Effect pages show their side card from 700 px: the settings column is narrowest just
    /// above that. The tab pages with a 1000 px side card are narrowest at 1000.</summary>
    private static (double usual, double narrow) Widths(string page) => page switch
    {
        "haptics" or "lockdown" or "awareness" => (1100, 1000),
        _ when page.StartsWith("settings-", StringComparison.Ordinal) || page == "companion-picker" => (900, 720),
        _ => (1050, 720),
    };

    private static bool OnRow(ComboBox c)
    {
        if (c.Parent is Panel p && p.Children.OfType<RowLabel>().Any()) return true;
        for (DependencyObject? d = c; d != null; d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent)
            if (d is Grid g && g.Style != null && ReferenceEquals(g.Style, Application.Current.TryFindResource("OptionRow")))
                return true;
        return false;
    }

    // ----- the items the code fills in, in the current language (mirrors each page's fill method) -----

    /// <summary>A hardware or user-made name: the box may trim it (whole value in the tool tip).</summary>
    private sealed class FreeText
    {
        public FreeText(string name) { Name = name; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    private static string Monitor(int n, bool primary, int w, int h)
        => $"{Loc.Get("monitor_label")} {n} ({(primary ? Loc.Get("monitor_primary_marker") + ", " : "")}{w}x{h})";

    private static object Invoke(object target, string method, params object[] args)
    {
        var t = target as Type ?? target.GetType();
        var m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(t.Name, method);
        return m.Invoke(target is Type ? null : target, args)!;
    }

    /// <summary>
    /// Fills a code-filled box with what its page's code puts there (the longest realistic values),
    /// or leaves a XAML-filled box alone. Returns the names that are free text.
    /// </summary>
    private static void FillLikeTheCode(FrameworkElement page, ComboBox c)
    {
        void Set(params object[] items)
        {
            c.ItemsSource = null;
            c.Items.Clear();
            foreach (var i in items)
                c.Items.Add(i is FreeText ft ? new ComboBoxItem { Content = ft.Name, Tag = ft } : new ComboBoxItem { Content = i });
        }
        switch (c.Name)
        {
            case "CmbMonitor":
                Set(Loc.Get("monitor_target_default"), Loc.Get("monitor_target_all"), Monitor(1, true, 3840, 2160), Monitor(2, false, 2560, 1440));
                break;
            case "CmbContentMonitor":
                Set(Loc.Get("monitor_target_all"), Loc.Get("monitor_target_primary_only"), Monitor(1, true, 3840, 2160), Monitor(2, false, 2560, 1440));
                break;
            case "CmbWebcamMonitor":
                Set(Loc.Get("webcam_monitor_primary"),
                    new FreeText(string.Format(Loc.Get("webcam_monitor_item_fmt"), 2, @"\\.\DISPLAY12", 3840, 2160)));
                break;
            case "CmbAfter":
                Set(Enumerable.Range(Models.AppSettings.MercyAfterFailsMin,
                        Models.AppSettings.MercyAfterFailsMax - Models.AppSettings.MercyAfterFailsMin + 1)
                    .Select(n => (object)Loc.GetF("setting_mercy_after_n", n)).ToArray());
                break;
            case "CmbCreepSpeed":
                Set(Loc.Get("super_creep_speed_slow"), Loc.Get("super_creep_speed_normal"), Loc.Get("super_creep_speed_fast"));
                break;
            case "CmbPoolSpiral":
                Set(Loc.Get("super_pool_spiral_new"), new FreeText("pink tunnel night drive"));
                break;
            case "CmbAppearance":
                Set(Loc.Get("video_appearance_fullscreen"), Loc.Get("video_appearance_floating"));
                break;
            case "CmbFloatingCount":
                Set("1", "2", "3", "4");
                break;
            case "CmbSpice":
                Set(Loc.Get("emi_desk_spice_innocent"), Loc.Get("emi_desk_spice_suggestive"), Loc.Get("emi_desk_spice_anything"));
                break;
            case "CmbGentleFade":
            case "CmbGentleSpeed":
                if (c.Items.Count == 0) Invoke(page, "FillGentleChoices");
                break;
            case "CmbLanguageSetting":
                MainWindow.FillLanguageCombo(c, shortLabels: false);
                break;
            case "CmbMotion" when page is BubblePopFeatureControl:
                c.Items.Clear();
                foreach (var (style, key, v2) in new[]
                         {
                             (Models.BubbleMotionStyle.FloatUp, "bubble_motion_float_up", false),
                             (Models.BubbleMotionStyle.Rain, "bubble_motion_rain", true),
                             (Models.BubbleMotionStyle.SpiralIn, "bubble_motion_spiral_in", true),
                             (Models.BubbleMotionStyle.Mix, "bubble_motion_mix", false),
                         })
                    c.Items.Add(Invoke(typeof(BubblePopFeatureControl), "MotionItem", style, key, v2));
                break;
            case "CmbMotion" when page is FlashFeatureControl:
                c.Items.Clear();
                Invoke(page, "AddMotionChoice", Models.FlashMotionStyle.Still, "option_flash_motion_still", false);
                Invoke(page, "AddMotionChoice", Models.FlashMotionStyle.DriftBounce, "option_flash_motion_drift", true);
                Invoke(page, "AddMotionChoice", Models.FlashMotionStyle.Pendulum, "option_flash_motion_pendulum", true);
                Invoke(page, "AddMotionChoice", Models.FlashMotionStyle.Mix, "option_flash_motion_mix", false);
                break;
            case "CmbExit":
                c.Items.Clear();
                Invoke(page, "SelectExit", Models.FlashExitStyle.Melt);
                break;
            case "CmbFont":
                Set("Segoe UI", new FreeText("Segoe UI Variable Display Semibold"));
                break;
            case "CmbAudioOutputDevice":
                // MainWindow.UiUpdates binds AudioOutputDevice objects by Name.
                c.Items.Clear();
                c.ItemsSource = new List<object> { new FreeText("System default"), new FreeText("Headphones (2- Realtek(R) High Definition Audio)") };
                c.DisplayMemberPath = nameof(FreeText.Name);
                break;
            case "CmbMicDevice":
                Set(new FreeText("Microphone Array (Intel Smart Sound Technology)"));
                break;
            case "CmbWebcamDevice":
                Set(new FreeText("Integrated Webcam HD (USB2.0 UVC)"));
                break;
            case "CmbAvatar":
                Set(new FreeText("Classic (animated tube)"));
                break;
            case "CmbPersonality":
                Set(new FreeText("Pushy charm (house default)"));
                break;
        }
    }

    private static bool IsFree(object? item) => item is FreeText || (item as ComboBoxItem)?.Tag is FreeText;

    private static IEnumerable<object> ItemsOf(ComboBox c) => c.ItemsSource?.Cast<object>() ?? c.Items.Cast<object>();

    /// <summary>
    /// What is wrong with one box at the current width: a value cut with no tool tip to read it
    /// (a value the app's own strings make must show whole at the page's usual width), a box that takes
    /// more than its share of the row, or a row label squeezed past readable.
    /// </summary>
    private static IEnumerable<string> Check(ComboBox c, bool usualWidth)
    {
        string name = string.IsNullOrEmpty(c.Name) ? "(unnamed)" : c.Name;
        if (c.Visibility != Visibility.Visible || c.ActualWidth <= 0) yield break;
        if (!RowComboFit.GetFit(c)) { yield return $"{name}: not fitted"; yield break; }
        double room = RowComboFit.Room(c);
        foreach (var item in ItemsOf(c))
        {
            string? text = RowComboFit.TextOf(c, item);
            if (string.IsNullOrEmpty(text)) continue;
            double w = RowComboFit.ItemWidth(c, item);
            if (w <= room + 0.5) continue;
            bool visualCopy = ((item as ComboBoxItem)?.Content) is UIElement;
            if (visualCopy) yield return $"{name}: '{text}' needs {w:0} of {room:0} and a drawn item cannot trim";
            else if (usualWidth && !IsFree(item)) yield return $"{name}: '{text}' needs {w:0} of {room:0}";
        }
        if (VisualTreeHelper.GetParent(c) is Grid g && g.ActualWidth > 0)
        {
            double share = Math.Max(double.IsNaN(c.Width) ? 0 : c.Width, Math.Max(c.MinWidth, g.ActualWidth * RowComboFit.RowShare));
            // A box that stretches across its own column (device pickers) is the column's width by design.
            bool stretches = double.IsNaN(c.Width) && c.HorizontalAlignment == HorizontalAlignment.Stretch;
            if (!stretches && c.ActualWidth > share + 1) yield return $"{name}: takes {c.ActualWidth:0} of a {g.ActualWidth:0} px row";
            foreach (var label in g.Children.OfType<RowLabel>())
                if (RowsRender.Clipped(label) is { } clip) yield return $"{name} row: {clip}";
        }
    }

    private static List<string> CheckPage(string page, string lang)
    {
        var problems = new List<string>();
        RowsRender.InLanguage(lang, () =>
        {
            var el = Make(page);
            var (usual, narrow) = Widths(page);
            var rowStyle = Application.Current.TryFindResource("RowComboBoxStyle");
            var host = RowsRender.Host(el, usual, before: p =>
            {
                foreach (var c in RowsRender.Descendants(p).OfType<ComboBox>().Where(OnRow).ToList())
                {
                    FillLikeTheCode(p, c);
                    // The widest value is the one that would be cut.
                    var items = ItemsOf(c).ToList();
                    if (items.Count > 0)
                        c.SelectedItem = items.OrderByDescending(i => RowComboFit.ItemWidth(c, i)).First();
                }
            });
            var combos = RowsRender.Descendants(el).OfType<ComboBox>().Where(OnRow).ToList();
            foreach (var (width, isUsual) in new[] { (usual, true), (narrow, false) })
            {
                host.Width = width;
                RowsRender.Layout(host, width);
                foreach (var c in combos) RowComboFit.Refresh(c);
                RowsRender.Layout(host, width);
                foreach (var c in combos)
                {
                    if (!ReferenceEquals(c.Style, rowStyle)) { problems.Add($"{lang} {width}: {c.Name}: not RowComboBoxStyle"); continue; }
                    if (Math.Abs(c.FontSize - 14) > 0.01 || c.FontWeight != FontWeights.Bold)
                        problems.Add($"{lang} {width}: {c.Name}: {c.FontSize} {c.FontWeight}");
                    problems.AddRange(Check(c, isUsual).Select(s => $"{lang} {width}: {s}"));
                }
            }
            return 0;
        });
        return problems;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_combo_on_a_rows_row_shows_its_values_in_every_language(string page)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var problems = new List<string>();
            foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
                problems.AddRange(CheckPage(page, code));
            Assert.True(problems.Count == 0, page + ":\n" + string.Join("\n", problems));
        });
        RowsRender.AssertSilent(page);
    }

    [Fact]
    public void Every_language_is_checked()
        => Assert.Equal(10, LocalizationManager.AvailableLanguages.Length);

    /// <summary>The reviewer's case, failing before the rule: the monitor default in English on Pink Filter.</summary>
    [Fact]
    public void The_monitor_default_shows_whole_on_pink_filter()
    {
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage("en", () =>
        {
            var page = new PinkFilterFeatureControl();
            var host = RowsRender.Host(page, 1050, before: p =>
            {
                var c = (ComboBox)p.FindName("CmbMonitor");
                FillLikeTheCode(p, c);
                c.SelectedIndex = 0;
            });
            var cmb = (ComboBox)page.FindName("CmbMonitor");
            RowComboFit.Refresh(cmb);
            RowsRender.Layout(host, 1050);
            double need = RowComboFit.ItemWidth(cmb, cmb.SelectedItem);
            Assert.True(need <= RowComboFit.Room(cmb) + 0.5, $"'Default (follow multi-monitor)' needs {need:0} of {RowComboFit.Room(cmb):0}");
            Assert.False(RowComboFit.SelectionCut(cmb));
            return 0;
        }));
    }

    /// <summary>Past the row's share the value ends in an ellipsis and the tool tip carries it whole,
    /// above the box's own tip; a short value gives the box its own tip back.</summary>
    [Fact]
    public void A_value_past_the_cap_trims_and_its_tip_shows_it_whole()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var style = (Style)Application.Current.TryFindResource("RowComboBoxStyle")!;
            var c = new ComboBox { Style = style, Width = 150 };
            c.SetBinding(FrameworkElement.ToolTipProperty,
                new Binding("[tooltip_devices_cam_pick]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            string authored = (string)c.ToolTip;
            const string longName = "Integrated Webcam HD with a very long vendor name (USB2.0 UVC)";
            c.Items.Add(new ComboBoxItem { Content = "Cam" });
            c.Items.Add(new ComboBoxItem { Content = longName });
            var row = new Grid { Width = 300 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(c, 1);
            row.Children.Add(c);
            c.SelectedIndex = 1;
            RowsRender.Layout(row, 300);
            RowComboFit.Refresh(c);
            RowsRender.Layout(row, 300);

            Assert.True(c.ActualWidth <= 300 * RowComboFit.RowShare + 1, $"box {c.ActualWidth:0} past 60% of 300");
            Assert.True(RowComboFit.SelectionCut(c));
            Assert.Equal(TextTrimming.CharacterEllipsis, RowComboFit.ValueText(c)!.TextTrimming);
            var tip = Assert.IsType<StackPanel>(c.ToolTip);
            var lines = tip.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Equal(longName, lines[0]);
            Assert.Contains(authored, lines);

            c.SelectedIndex = 0;
            RowComboFit.Refresh(c);
            Assert.Equal(authored, c.ToolTip);
            Assert.NotNull(BindingOperations.GetBindingBase(c, FrameworkElement.ToolTipProperty));
        });
    }

    /// <summary>A box that fits keeps its authored Width: the rule only ever grows a box.</summary>
    [Fact]
    public void A_box_whose_values_fit_keeps_its_width()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var style = (Style)Application.Current.TryFindResource("RowComboBoxStyle")!;
            var c = new ComboBox { Style = style, Width = 150 };
            c.Items.Add(new ComboBoxItem { Content = "Full" });
            c.Items.Add(new ComboBoxItem { Content = "Off" });
            var row = new Grid { Width = 600 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(c, 1);
            row.Children.Add(c);
            RowsRender.Layout(row, 600);
            RowComboFit.Refresh(c);
            RowsRender.Layout(row, 600);
            Assert.Equal(150, c.ActualWidth, 1);
            Assert.Null(c.ToolTip);
        });
    }

    [Fact]
    public void The_compact_dark_combo_keeps_its_size_off_the_rows()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var dark = (Style)Application.Current.TryFindResource("DarkComboBoxStyle")!;
            var c = new ComboBox { Style = dark };
            var host = new StackPanel();
            host.Children.Add(c);
            host.Measure(new Size(300, 100));
            Assert.Equal(11, c.FontSize, 2);
            Assert.False(RowComboFit.GetFit(c));
        });
    }

    /// <summary>With CCP_ROWS_DUMP set: stills of every rows page with each combo filled like the code and
    /// showing its widest value, at the usual and the narrowest width (CCP_ROWS_LANG picks the language).</summary>
    [Theory]
    [MemberData(nameof(Pages))]
    public void Combo_stills_when_asked(string page)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CCP_ROWS_DUMP"))) return;
        string? lang = Environment.GetEnvironmentVariable("CCP_ROWS_LANG");
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(lang, () =>
        {
            var el = Make(page);
            var (usual, narrow) = Widths(page);
            var host = RowsRender.Host(el, usual, before: p =>
            {
                foreach (var c in RowsRender.Descendants(p).OfType<ComboBox>().Where(OnRow).ToList())
                {
                    FillLikeTheCode(p, c);
                    var items = ItemsOf(c).ToList();
                    if (items.Count > 0) c.SelectedItem = items.OrderByDescending(i => RowComboFit.ItemWidth(c, i)).First();
                }
            });
            string suffix = string.IsNullOrWhiteSpace(lang) ? "" : "-" + lang;
            foreach (var w in new[] { usual, narrow })
            {
                host.Width = w;
                RowsRender.Layout(host, w);
                foreach (var c in RowsRender.Descendants(el).OfType<ComboBox>()) RowComboFit.Refresh(c);
                RowsRender.Layout(host, w);
                RowsRender.Dump(host, $"combo-{page}-{w:0}{suffix}");
            }
            return 0;
        }));
        RowsRender.AssertSilent(page);
    }
}
