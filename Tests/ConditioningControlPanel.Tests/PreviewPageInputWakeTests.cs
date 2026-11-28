using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Preview;
using ConditioningControlPanel.Controls.Preview.Scenes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Live Preview on tabs (owner 2026-11-24): the Haptics tab keeps most of its values in a plain nested
/// object that raises no change event, so its preview opts in to waking whenever a slider, switch or
/// dropdown of its own page is touched (<see cref="EffectPreview.WakeOnPageInput"/>).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PreviewPageInputWakeTests
{
    [Fact]
    public void A_touched_control_of_the_page_wakes_the_preview_and_it_sleeps_again_after()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            double now = 0;
            var slider = new Slider { Minimum = 0, Maximum = 10, Value = 1 };
            var toggle = new CheckBox();
            var combo = new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 };
            var preview = new EffectPreview { Width = 300, Height = 300 };
            var panel = new StackPanel();
            panel.Children.Add(slider);
            panel.Children.Add(toggle);
            panel.Children.Add(combo);
            panel.Children.Add(preview);
            var page = new UserControl { Content = panel, Width = 400, Height = 600 };
            page.Measure(new Size(400, 600));
            page.Arrange(new Rect(0, 0, 400, 600));

            preview.UseScene(new MindWipePreviewScene(), () => now);
            preview.HookPageInput(page);
            var driver = preview.Driver!;

            now = 100;
            Assert.False(driver.IsAwake);

            slider.Value = 5;
            Assert.True(driver.IsAwake);
            now += PreviewDriver.WakeSeconds + 1;
            Assert.False(driver.IsAwake);

            toggle.IsChecked = true;
            Assert.True(driver.IsAwake);
            now += PreviewDriver.WakeSeconds + 1;
            Assert.False(driver.IsAwake);

            combo.SelectedIndex = 1;
            Assert.True(driver.IsAwake);
        });
    }

    [Fact]
    public void The_Haptics_tab_opts_in_and_no_Studio_page_does()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + System.AppContext.BaseDirectory);
        var root = System.IO.Path.Combine(dir!.FullName, "ConditioningControlPanel");
        var haptics = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Views", "Tabs", "HapticsTabView.xaml"));
        Assert.Contains("Scene=\"haptics\" WakeOnPageInput=\"True\"", haptics);
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "Features"), "*FeatureControl.xaml"))
            Assert.DoesNotContain("WakeOnPageInput", System.IO.File.ReadAllText(f));
    }
}
