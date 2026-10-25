using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

// The assembly's TestUserDataProfile + IsolateProcessState hook sandbox and restore all settings/seams.
public sealed class MiscEvidenceTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void UpdateProgressClampsAndPaintsTheDownloadPercentage() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var dialog = new UpdateProgressDialog();
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            var fill = dialog.FindControl<Border>("ProgressFill")!;
            var track = (Border)((Control)fill.Parent!).Parent!;
            var width = track.Bounds.Width - 6; // WPF UpdateProgressDialog.xaml.cs:28
            Assert.True(width > 0);
            Assert.Equal(Loc.Get("dialog_downloading_update"), dialog.Title);
            Assert.False(dialog.CanResize);
            foreach (var (fraction, expected) in new[] { (-0.1, 0), (0.37, 37), (1.2, 100), (0.0, 0) })
            {
                dialog.SetProgress(fraction, ((Control)fill.Parent!).Bounds.Width);
                Assert.Equal($"{expected}%", dialog.FindControl<TextBlock>("TxtProgress")!.Text);
                Assert.Equal(width * expected / 100, fill.Width, 6);
            }
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public void PinkFilterControlsWriteSettingsAndTheTintWindowPaintsTheResetColour() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        s.PinkFilterEnabled = false;
        s.PinkFilterOpacity = 10;
        s.PinkFilterColor = "#123456";
        s.PinkFilterTargetMonitor = 99; // unplugged: population must preserve the saved target
        var panel = new PinkFilterFeatureControl();
        var host = new Window { Content = panel };
        var tint = new TintOverlayWindow();
        try
        {
            host.Show();
            Assert.Equal(99, s.PinkFilterTargetMonitor);
            Assert.Equal(0, panel.FindControl<ComboBox>("CmbMonitor")!.SelectedIndex);
            panel.FindControl<CheckBox>("ChkEnable")!.IsChecked = true;
            Assert.True(s.PinkFilterEnabled);
            panel.FindControl<Slider>("SliderOpacity")!.Value = 42;
            Assert.Equal(42, s.PinkFilterOpacity);
            Assert.Equal("42%", panel.FindControl<TextBlock>("TxtOpacity")!.Text);
            panel.FindControl<ComboBox>("CmbMonitor")!.SelectedIndex = 1;
            Assert.Equal(-2, s.PinkFilterTargetMonitor); // All
            Assert.Equal((byte)0x12, PinkFilterOverlay.EffectiveColor().R);
            panel.FindControl<Button>("BtnResetColor")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("", s.PinkFilterColor);
            var rgb = CoreMods.GetFilterColorRgb();
            Assert.Equal(rgb, PinkFilterOverlay.EffectiveColor());
            Assert.Equal(Color.FromRgb(rgb.R, rgb.G, rgb.B),
                Assert.IsType<SolidColorBrush>(panel.FindControl<Border>("ColorSwatch")!.Background).Color);
            // Refresh's own SetTint call (PinkFilterOverlay.cs:92/115) needs an X11 overlay, which headless lacks.
            tint.SetTint(rgb.R, rgb.G, rgb.B, s.PinkFilterOpacity / 100.0);
            Assert.Equal(Color.FromArgb((byte)(0.42 * 255), rgb.R, rgb.G, rgb.B),
                Assert.IsType<SolidColorBrush>(Assert.IsType<Border>(tint.Content).Background).Color);
            s.PinkFilterOpacity = 27;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(27, panel.FindControl<Slider>("SliderOpacity")!.Value);
            Assert.Equal("27%", panel.FindControl<TextBlock>("TxtOpacity")!.Text);
        }
        finally { host.Close(); tint.Close(); PinkFilterOverlay.CloseAll(); }
    });

    [Fact]
    public void IntifaceConnectCorrectsTheAddressAndRejectsInvalidInput() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current.Haptics;
        s.EnsureV2Migrated();
        s.V2.Provider("buttplug").Enabled = true;
        s.V2.Provider("lovense").Enabled = false;
        s.V2.Provider("mock").Enabled = false;
        using var service = new HapticService(s);
        CoreHaptics.Service = service;
        var page = new HapticsTabView();
        var host = new Window { Content = page };
        try
        {
            host.Show();
            page.TxtHapticIntifaceUrl.Text = "ftp://invalid";
            Dispatcher.UIThread.RunJobs(); // TextChanged is deferred, just as between typing and clicking
            page.BtnHapticConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var warning = Assert.Single(host.OwnedWindows);
            Assert.Contains(Loc.GetF("haptics_intiface_url_invalid", "ftp://invalid"),
                string.Join(" ", warning.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
            Assert.Equal("ftp://invalid", s.ButtplugUrl);
            Assert.True(page.BtnHapticConnect.IsEnabled);
            Assert.False(service.IsConnected);
            warning.Close();
            // A fixable address is corrected before the connect starts; the connect itself (closed loopback
            // port, never a real device) is not awaited, so no wall-clock wait (P08).
            page.TxtHapticIntifaceUrl.Text = "127.0.0.1.1";
            Dispatcher.UIThread.RunJobs(); // TextChanged is deferred, just as between typing and clicking
            page.BtnHapticConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("ws://127.0.0.1:1", page.TxtHapticIntifaceUrl.Text);
            Assert.Equal("ws://127.0.0.1:1", s.ButtplugUrl);
            Assert.False(page.BtnHapticConnect.IsEnabled); // connecting
        }
        finally { foreach (var w in host.OwnedWindows.ToArray()) w.Close(); host.Close(); service.PanicStop(); }
    });

    [Fact]
    public void ShippedDefaultBarkReachesTheTubeAudioSinkAndMutedEggIsTextOnly() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        (s.AvatarEnabled, s.AvatarMuted, s.CompanionVoiceLinesMuted, s.MasterVolume, s.BarkChatSuppressionMs) = (true, false, false, 80, 0);
        CoreMods.ActiveModIdProvider = () => "builtin-ccp-default";
        CoreMods.ActiveModPackageProvider = () => null;
        var source = Path.Combine(Root(), "Assets", "sounds", "companion_audio", "mods", "builtin-ccp-default");
        var manifest = Path.Combine(source, "bark_rules.json");
        // Load the actual shipped rule; isolate it from unrelated, higher-priority navigation barks.
        var rule = BarkRuleManifest.Load(manifest, null).AllRules.Single(r => r.Id == "nav_home");
        var variant = Assert.Single(rule.VariantPool!);
        var dest = Path.Combine(ContentLocator.ContentRoot, "Resources", "sounds", "companion_audio", "mods", "builtin-ccp-default");
        Directory.CreateDirectory(dest);
        var clip = Path.Combine(dest, variant.Audio!);
        File.Copy(Path.Combine(source, variant.Audio!), clip, true);
        string? played = null;
        string? audioTag = null;
        CoreAudio.PlayStoppableProvider = (path, _, tag, _, _) => { played = path; audioTag = tag; return () => { }; };
        var tube = new AvatarTubeWindow(null);
        var findTube = BarkHead.FindTube;
        BarkHead.FindTube = () => tube;
        var seams = (CoreModsHooks.ReloadBarkRules, CoreSettingsHooks.SettingChangedSink, CoreBark.UiAction, CoreBark.TabNavigated,
            CoreBark.FeatureOpened, CoreBark.AvatarClicked, CoreBark.ChaosDraftAutopick, CoreBark.ChaosRankUp, CoreBark.ChaosResultsShown, CoreBark.AllLinesProvider);
        // The real head wiring (BarkHead.Seed) and the shell's doorbell; the rule's 0.2 chance is pinned to a hit.
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule }), new AlwaysRandom());
        BarkHead.Seed(engine);
        try
        {
            tube.Show();
            engine.Start();
            CoreBark.NotifyTabNavigated("settings");
            Dispatcher.UIThread.RunJobs();
            var installed = Path.Combine(ContentLocator.InstallRoot, "Resources", "sounds", "companion_audio", "mods", "builtin-ccp-default", variant.Audio!);
            Assert.Equal(File.Exists(installed) ? installed : clip, played);
            Assert.Equal("bark-voice", audioTag);
            Assert.Equal(variant.Text, tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(0, tube.QueuedSpeechCount);
            tube.Close();
            tube = new AvatarTubeWindow(null); // independent idle tube: no previous speech gap
            tube.Show();
            played = null;
            s.MasterVolume = 0;
            var egg = BarkRuleManifest.Load(manifest, null).AllRules.Single(r => r.Id == "egg_avatar_pop_flash");
            engine = new BarkEngine(() => new BarkRuleSet(new[] { egg })) { Speak = BarkHead.Speak };
            engine.Start();
            Assert.True(engine.Raise("AvatarBubbleEgg", guaranteed: true));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(played);
            Assert.Equal(Assert.Single(egg.VariantPool!).Text, tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }
        finally
        {
            tube.Close(); BarkHead.FindTube = findTube; File.Delete(clip);
            (CoreModsHooks.ReloadBarkRules, CoreSettingsHooks.SettingChangedSink, CoreBark.UiAction, CoreBark.TabNavigated,
                CoreBark.FeatureOpened, CoreBark.AvatarClicked, CoreBark.ChaosDraftAutopick, CoreBark.ChaosRankUp, CoreBark.ChaosResultsShown, CoreBark.AllLinesProvider) = seams;
        }
    });

    private sealed class AlwaysRandom : Random { protected override double Sample() => 0; public override double NextDouble() => 0; }

    private static string Root([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
