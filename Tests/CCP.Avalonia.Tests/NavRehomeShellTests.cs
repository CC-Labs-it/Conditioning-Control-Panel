using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The nav rework's rehomed surfaces through the shell (WPF NavRehomeTests, 3fc1ae652; lane
/// sync6-nav-rail-d): the Home System bubble opens Settings > Monitors, the one home of the monitor
/// picker and the four video rows; Library > Folders paints the assets folder chip; every strip pill
/// wears a "?" badge beside it (d0ec7aa8d); the rail medallions follow the active mod's art.
/// </summary>
public sealed class NavRehomeShellTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public async Task SystemBubbleOpensMonitors_TheOneHomeOfThePickerAndVideoRows()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            bool fillBefore = s.FillAllMonitorsWithVideo;
            var shellBefore = ExternalOpener.Shell;
            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();

                w.ShowTab("settings");
                var dash = w.Named<SettingsTabView>("SettingsTab")!;
                dash.FindControl<Button>("VelvetBtnSystem")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("appsettings", w.CurrentTab);
                var page = w.Named<AppSettingsTabView>("AppSettingsTab")!;
                Assert.Equal("monitors", page.CheckedSectionKey);
                // WPF OpenAppSettingsSection scrolls the section into view, not just the pill lit.
                var scroll = page.FindControl<ScrollViewer>("SectionScroll")!;
                var stack = page.FindControl<StackPanel>("SectionStack")!;
                var monY = page.FindControl<Control>("SectionMonitors")!.TranslatePoint(default, stack)!.Value.Y;
                Assert.True(monY > 0);
                Assert.True(scroll.Offset.Y >= monY - 4 - 0.5, $"offset {scroll.Offset.Y} < Monitors y {monY}");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("monitors", page.CheckedSectionKey);
                Assert.EndsWith(Loc.Get("settings_section_monitors"), w.Named<SectionTabStrip>("SectionStrip")!.CrumbText);

                // Monitors is the one home of the picker (WPF 5a3123f71): General no longer draws it.
                var monitors = page.FindControl<MonitorsSettingsSection>("SectionMonitors")!;
                Assert.Single(monitors.GetVisualDescendants().OfType<MonitorTargetPicker>());
                Assert.Empty(page.FindControl<GeneralSettingsSection>("SectionGeneral")!.GetVisualDescendants().OfType<MonitorTargetPicker>());

                // A row writes its setting, and a write from elsewhere repaints the row.
                var fill = monitors.FindControl<CheckBox>("ChkFillAllMon")!;
                fill.IsChecked = !fillBefore;
                Assert.Equal(!fillBefore, s.FillAllMonitorsWithVideo);
                s.FillAllMonitorsWithVideo = fillBefore;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(fillBefore, fill.IsChecked);

                // Settings > Data > Open logs folder (WPF 7e50c9381) hands the logs folder to the opener.
                var logs = Path.Combine(CorePaths.UserData, "logs");
                Directory.CreateDirectory(logs);
                string? opened = null;
                ExternalOpener.Shell = t => { opened = t; return true; };
                w.OpenAppSettingsSection("data");
                page.FindControl<DataSettingsSection>("SectionData")!.FindControl<Button>("BtnOpenLogsFolder")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(logs, opened);
            }
            finally
            {
                ExternalOpener.Shell = shellBefore;
                s.FillAllMonitorsWithVideo = fillBefore;
                w?.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FoldersPillLandsOnTheAssetsFolderChip_AndEveryPillWearsAHelpBadge()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var motionBefore = CoreSettings.Current.MotionLevel;
            MainShellWindow? w = null;
            try
            {
                CoreSettings.Current.MotionLevel = MotionLevel.Full;   // NavGlow is off under Off
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();

                w.ShowTab("assets");
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                // Already on the page, so only the zone landing (ScrollToZone) can repaint the chip.
                w.Named<AssetsTabView>("AssetsTab")!.FindControl<TextBlock>("TxtAssetsFolderPath")!.Text = "";
                strip.PillFor("folders")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("folders", w.CurrentTab);
                var assets = w.Named<AssetsTabView>("AssetsTab")!;
                Assert.True(assets.IsVisible);
                Assert.Equal(CorePaths.EffectiveAssets, assets.FindControl<TextBlock>("TxtAssetsFolderPath")!.Text);
                // The landing glows the chip and the preset picker once (WPF AssetsTabView :25-68).
                Assert.Equal(1, Glows(assets.FindControl<Control>("ZoneFolders")!));
                Assert.Equal(1, Glows(assets.FindControl<Control>("CmbAssetPresets")!));

                // Every pill has a "?" badge, a sibling of the pill (never inside it), saying what the page does.
                foreach (var key in strip.PillKeys)
                {
                    var pill = strip.PillFor(key)!;
                    var badge = strip.HelpBadgeFor(key);
                    Assert.NotNull(badge);
                    Assert.Same(pill.Parent, badge!.Parent);
                    Assert.DoesNotContain(badge, pill.GetVisualDescendants());
                    var help = Loc.Get("nav_help_" + key);
                    var card = Assert.IsAssignableFrom<Panel>(ToolTip.GetTip(badge));
                    Assert.Contains(card.Children.OfType<TextBlock>(), t => t.Text == help);
                    Assert.Contains(help, (string)ToolTip.GetTip(pill)!);
                }

                // The host stays the pill's size: the badge pokes out with negative margins.
                w.UpdateLayout();
                var host = (Control)strip.PillFor("assets")!.Parent!;
                Assert.Equal(strip.PillFor("assets")!.Bounds.Size, host.Bounds.Size);
            }
            finally { w?.Close(); CoreSettings.Current.MotionLevel = motionBefore; }
            return Task.CompletedTask;
        });
    }

    private static int Glows(Visual v) =>
        global::Avalonia.Controls.Primitives.AdornerLayer.GetAdornerLayer(v)?.Children
            .OfType<global::ConditioningControlPanel.Avalonia.Controls.NavGlowRing>()
            .Count(r => global::Avalonia.Controls.Primitives.AdornerLayer.GetAdornedElement(r) == v) ?? 0;

    [Fact]
    public async Task RailMedallionsWearTheActiveModsDoorArt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var dir = Directory.CreateTempSubdirectory();
            var png = Path.Combine(dir.FullName, "door.png");
            using (var bmp = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bmp.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            MainShellWindow? w = null;
            try
            {
                int before = ModChangedCount();
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var play = w.Named<Image>("ImgDoorPlay")!;
                Assert.Equal(play.Source!.Size.Width, play.Source.Size.Height); // the shipped square medallion
                // The shipped Social door (Assets/nav/door_social.png, linked on both heads) replaces the
                // placeholder and its shade, as WPF ApplyDoorArt resolves it at startup.
                Assert.False(w.Named<Border>("ShadeDoorSocial")!.IsVisible);

                CoreModArt.OverridePathProvider = p => p is "nav/door_play.png" or "nav/door_social.png" ? png : null;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new PixelSize(128, 64), ((Bitmap)play.Source!).PixelSize); // the mod's 2:1 art at the 128px cap
                Assert.Equal(new PixelSize(128, 64), ((Bitmap)w.Named<Image>("ImgDoorSocial")!.Source!).PixelSize);

                w.Close();
                Assert.Equal(before, ModChangedCount()); // the window leaves with its subscriber (P68)
            }
            finally
            {
                CoreModArt.OverridePathProvider = previous;
                w?.Close();
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    private static int ModChangedCount()
        => (typeof(CoreMods).GetField(nameof(CoreMods.ModChanged), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null) as System.Delegate)?.GetInvocationList().Length ?? 0;
}
