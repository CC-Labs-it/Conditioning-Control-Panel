using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Studio option sections (Features/OptionSection): the remembered-state rules, the motion
/// timing, and every sectioned Studio page realized open and closed. Set <c>CCP_SECTION_DUMP</c>
/// to a folder to also write a PNG of each page both ways.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class OptionSectionTests
{
    // ---------------------------------------------------------------- remembered state

    [Fact]
    public void A_section_never_touched_uses_its_default()
    {
        var store = new Dictionary<string, bool>();
        Assert.True(OptionSectionState.Resolve(store, "flash.basics", defaultOpen: true));
        Assert.False(OptionSectionState.Resolve(store, "flash.gaze", defaultOpen: false));
        Assert.False(OptionSectionState.Resolve(null, "flash.gaze", defaultOpen: false));
    }

    [Fact]
    public void A_choice_is_remembered_per_key()
    {
        var store = new Dictionary<string, bool>();
        Assert.True(OptionSectionState.Remember(store, "flash.gaze", true));
        Assert.True(OptionSectionState.Remember(store, "flash.basics", false));
        Assert.True(OptionSectionState.Resolve(store, "flash.gaze", defaultOpen: false));
        Assert.False(OptionSectionState.Resolve(store, "flash.basics", defaultOpen: true));
        Assert.False(OptionSectionState.Resolve(store, "video.gaze", defaultOpen: false));
    }

    [Fact]
    public void Only_a_hand_choice_counts_as_a_choice()
    {
        var store = new Dictionary<string, bool>();
        Assert.False(OptionSectionState.HasChoice(store, "braindrain.extras"));
        OptionSectionState.Remember(store, "braindrain.extras", false);
        Assert.True(OptionSectionState.HasChoice(store, "braindrain.extras"));
        Assert.False(OptionSectionState.HasChoice(store, ""));
        Assert.False(OptionSectionState.HasChoice(null, "braindrain.extras"));
    }

    [Fact]
    public void A_section_without_a_key_is_not_remembered()
    {
        var store = new Dictionary<string, bool>();
        Assert.False(OptionSectionState.Remember(store, "", true));
        Assert.False(OptionSectionState.Remember(store, null, true));
        Assert.Empty(store);
        Assert.True(OptionSectionState.Resolve(store, "", defaultOpen: true));
    }

    [Fact]
    public void The_open_state_stays_on_this_pc()
    {
        // Left out of the cloud backup, and a restore keeps this PC's own.
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.StudioSectionsOpen)));
        var current = new AppSettings();
        current.StudioSectionsOpen["bubblepop.gaze"] = true;
        var restored = new AppSettings();
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.True(restored.StudioSectionsOpen["bubblepop.gaze"]);
        Assert.NotSame(current.StudioSectionsOpen, restored.StudioSectionsOpen);
    }

    [Fact]
    public void A_settings_file_without_the_field_reads_an_empty_store()
    {
        var s = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>("{\"StudioSectionsOpen\":null}")!;
        Assert.NotNull(s.StudioSectionsOpen);
        Assert.Empty(s.StudioSectionsOpen);
    }

    // ---------------------------------------------------------------- motion

    [Fact]
    public void Motion_off_is_instant_and_gentle_only_lengthens()
    {
        Assert.Equal(0, OptionSection.DurationMs(220, MotionLevel.Off, gentle: false));
        Assert.Equal(0, OptionSection.DurationMs(220, MotionLevel.Off, gentle: true));
        Assert.Equal(220, OptionSection.DurationMs(220, MotionLevel.Full, gentle: false));
        Assert.Equal(110, OptionSection.DurationMs(220, MotionLevel.Reduced, gentle: false));
        Assert.True(OptionSection.DurationMs(220, MotionLevel.Reduced, gentle: true) >= 220);
    }

    [Fact]
    public void Each_theme_wears_an_existing_hue()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (SectionTheme theme in Enum.GetValues(typeof(SectionTheme)))
                Assert.IsAssignableFrom<SolidColorBrush>(Application.Current.TryFindResource(OptionSection.HueKey(theme)));
        });
    }

    // ---------------------------------------------------------------- the control

    [Fact]
    public void Rows_written_inside_keep_their_names_and_land_in_the_body()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BubblePopFeatureControl();
            Realize(page);
            var gaze = page.FindName("ChkBubbleGazePop") as CheckBox;
            Assert.NotNull(gaze);
            var section = Ancestors(gaze!).OfType<OptionSection>().FirstOrDefault();
            Assert.NotNull(section);
            Assert.Contains(section!.Rows.Cast<UIElement>(), r => Ancestors(gaze!).Contains(r) || ReferenceEquals(r, gaze));
            // The hint keeps the Visibility its page gave it: the section only hides its body.
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)page.FindName("TxtBubbleGazeHint")).Visibility);
        });
    }

    [Fact]
    public void A_closed_section_counts_the_switches_left_on()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BubblePopFeatureControl();
            Realize(page);
            var gaze = (CheckBox)page.FindName("ChkBubbleGazePop");
            var section = Ancestors(gaze).OfType<OptionSection>().First();
            section.IsOpen = false;
            gaze.IsChecked = true;
            Assert.Equal(1, section.SwitchesOn);
            gaze.IsChecked = false;
            Assert.Equal(0, section.SwitchesOn);
        });
    }

    // ---------------------------------------------------------------- changed values in a closed section

    [Fact]
    public void The_closed_summary_reads_switches_and_changed_values()
    {
        Assert.Equal("", OptionSection.SummaryText(0, 0, "{0} on", "{0} changed"));
        Assert.Equal("2 on", OptionSection.SummaryText(2, 0, "{0} on", "{0} changed"));
        Assert.Equal("1 changed", OptionSection.SummaryText(0, 1, "{0} on", "{0} changed"));
        Assert.Equal("2 on · 1 changed", OptionSection.SummaryText(2, 1, "{0} on", "{0} changed"));
        Assert.Equal("1 changed", OptionSection.SummaryText(0, 1, null, "{oops"));   // a broken translation falls back
    }

    [Fact]
    public void Numbers_compare_as_numbers()
    {
        Assert.True(OptionSection.SameValue(0.3, 0.1 + 0.2));
        Assert.True(OptionSection.SameValue(10, 10.0));
        Assert.False(OptionSection.SameValue(10, 11));
        Assert.True(OptionSection.SameValue(MotionLevel.Full, MotionLevel.Full));
        Assert.False(OptionSection.SameValue("Arial", "Segoe UI"));
        Assert.True(OptionSection.SameValue(null, null));
        Assert.False(OptionSection.SameValue(null, 0));
    }

    [Fact]
    public void A_closed_section_marks_a_monitor_picker_off_its_default()
    {
        var keep = OptionSection.CurrentSettings;
        try
        {
            WpfRenderHarness.OnStaThread(() =>
            {
                var live = new AppSettings();
                OptionSection.CurrentSettings = () => live;
                var page = new PinkFilterFeatureControl();
                var host = Realize(page);
                var picker = (ComboBox)page.FindName("CmbMonitor");
                var section = Ancestors(picker).OfType<OptionSection>().First();
                section.IsOpen = false;
                section.Recount();
                Assert.Equal(0, section.ValuesChanged);
                live.PinkFilterTargetMonitor = 1;
                section.Recount();
                Assert.Equal(1, section.ValuesChanged);
                Assert.Contains("1", section.SummaryShown);
                // A host-hidden row never counts.
                picker.Visibility = Visibility.Collapsed;
                Assert.Equal(0, section.ValuesChanged);
                picker.Visibility = Visibility.Visible;
                section.Recount();
                host.UpdateLayout();
                Dump(host, "pinkfilter-extras-closed-changed");
                // Open: no summary.
                section.IsOpen = true;
                Assert.Equal("", section.SummaryShown);
            });
        }
        finally { OptionSection.CurrentSettings = keep; }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_tagged_row_names_a_real_setting(string name)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = PageFactories[name]();
            Realize(page);
            var tagged = Descendants(page).Where(d => OptionSection.GetSetting(d) is { Length: > 0 }).ToList();
            foreach (var d in tagged)
            {
                var setting = OptionSection.GetSetting(d)!;
                var p = typeof(AppSettings).GetProperty(setting);
                Assert.True(p is { CanRead: true }, $"{name}: {setting} is not an AppSettings property");
            }
            // Every slider and picker inside a section is tagged, or listed here as left out on purpose.
            var untagged = Descendants(page).OfType<OptionSection>()
                .SelectMany(s => Descendants(s)).Where(d => d is Slider or ComboBox)
                .Where(d => OptionSection.GetSetting(d) is not { Length: > 0 })
                .Select(d => ((FrameworkElement)d).Name).ToList();
            Assert.True(untagged.Count == 0, $"{name}: untagged {string.Join(", ", untagged)}");
        });
    }

    public static IEnumerable<object[]> Pages() => PageFactories.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, Func<FrameworkElement>> PageFactories = new()
    {
        ["bubblepop"] = () => new BubblePopFeatureControl(),
        ["flash"] = () => new FlashFeatureControl(),
        ["video"] = () => new VideoFeatureControl(),
        ["subliminal"] = () => new SubliminalFeatureControl(),
        ["pinkfilter"] = () => new PinkFilterFeatureControl(),
        ["spiral"] = () => new SpiralFeatureControl(),
        ["lockcard"] = () => new LockCardFeatureControl(),
        ["bouncingtext"] = () => new BouncingTextFeatureControl(),
        ["visuals"] = () => new VisualsFeatureControl(),
        ["braindrain"] = () => new ConditioningControlPanel.Views.Controls.Studio.BrainDrainFeatureControl(),
        // The Scheduler page is left whole on purpose: three rows (switch, time range, days), nothing to fold.
        ["ramp"] = () => new IntensityRampFeatureControl(),
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_sectioned_page_is_shorter_closed_and_every_title_shows(string name)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = PageFactories[name]();
            var host = Realize(page);
            var sections = Descendants(page).OfType<OptionSection>().ToList();
            Assert.True(sections.Count >= 2, $"{name}: {sections.Count} sections");
            Assert.Equal(sections.Count, sections.Select(s => s.Key).Distinct().Count());
            Assert.All(sections, s => Assert.False(string.IsNullOrEmpty(s.Key)));

            foreach (var s in sections) s.IsOpen = true;
            host.UpdateLayout();
            double open = page.ActualHeight;
            Dump(host, name + "-open");

            foreach (var s in sections) s.IsOpen = false;
            host.UpdateLayout();
            double closed = page.ActualHeight;
            Dump(host, name + "-closed");

            foreach (var s in sections) s.IsOpen = s.DefaultOpen;
            host.UpdateLayout();
            Dump(host, name + "-default");

            Assert.True(closed < open - 40, $"{name}: open {open:0} px, closed {closed:0} px");
            foreach (var s in sections)
                Assert.False(string.IsNullOrWhiteSpace(s.Title), $"{name}/{s.Key}: no title");
        });
    }

    // ---------------------------------------------------------------- helpers

    private static Grid Realize(FrameworkElement page)
    {
        var host = new Grid { Width = 520, Background = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x22)) };
        host.Children.Add(page);
        host.Measure(new Size(520, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return host;
    }

    private static void Dump(Grid host, string name)
    {
        if (Environment.GetEnvironmentVariable("CCP_SECTION_DUMP") is not { Length: > 0 } dir) return;
        host.Measure(new Size(520, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        Directory.CreateDirectory(dir);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            yield return d;
            foreach (var x in Descendants(d)) yield return x;
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject el)
    {
        for (var p = LogicalTreeHelper.GetParent(el); p != null; p = LogicalTreeHelper.GetParent(p))
            yield return p;
    }
}
