using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The launcher's Media pill and dialog, the shortcut link, the tile shortcut button and
/// (WPF LauncherWindow.xaml :466/:553, Tiles.cs :163, LauncherMediaDialog).</summary>
public sealed class LauncherLinksTests
{
    private static void Run(Action body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.LauncherSoundEnabled = false;
            try { body(); }
            finally { CoreSettings.ServiceProvider = null; }
        });
    }

    private static (string Id, Button Btn)[] TileButtons(LauncherWindow w) =>
        w.FindControl<Panel>("GamesGrid")!.Children.OfType<Border>()
            .Select(t => ((string)t.Tag!, t.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "tile-shortcut")))).ToArray();

    [Fact]
    public void ShortcutLink_WritesThePanelShortcut_SaysHowItWent_ThenAsksAgain() => Run(() =>
    {
        var w = new LauncherWindow();
        try
        {
            Assert.Equal("launcher_add_shortcut", w.ShortcutLinkKey);
            string? asked = "unset";
            w.ShortcutWriter = id => { asked = id; return true; };
            w.FindControl<Button>("ShortcutLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(asked);   // null = the panel
            Assert.Equal("launcher_shortcut_added", w.ShortcutLinkKey);
            Assert.Equal(Loc.Get("launcher_shortcut_added"), w.FindControl<TextBlock>("ShortcutLinkText")!.Text);

            w.ShortcutWriter = _ => throw new IOException("disk");
            w.WriteShortcut("backroom");
            Assert.Equal("launcher_shortcut_failed", w.ShortcutLinkKey);
            w.ResetShortcutLink();   // the 3 s timer's tick
            Assert.Equal("launcher_add_shortcut", w.ShortcutLinkKey);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void EveryTile_CarriesAHiddenShortcutButton_ForItsOwnGame_ThatLockdownRefuses() => Run(() =>
    {
        var w = new LauncherWindow();
        var oldLd = LockdownService.Current;
        try
        {
            string? asked = null;
            w.ShortcutWriter = id => { asked = id; return true; };
            var buttons = TileButtons(w);
            Assert.NotEmpty(buttons);
            foreach (var (id, btn) in buttons)
            {
                Assert.Equal(0, btn.Opacity);   // unseen until the pointer is on the tile
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(id, asked);
            }

            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(5));
            asked = null;
            buttons[0].Btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(asked);
            ld.Deactivate(); ld.Dispose();
        }
        finally { LockdownService.Current = oldLd; w.Close(); }
    });

    [Fact]
    public void MediaPill_OpensTheDialogOverTheLauncher() => Run(() =>
    {
        var w = new LauncherWindow();
        try
        {
            w.Show();
            w.FindControl<Button>("BtnGear")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(w.OwnedWindows.OfType<LauncherMediaDialog>()).Close();
        }
        finally { w.Close(); }
    });

    [Fact]
    public void MediaDialog_PresetGoesThroughThePanel_SoTheAssetsPageRepaints() => Run(() =>
    {
        var s = CoreSettings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.MediaSource, s.RemoteMediaConsented) = (true, true, "local", true);
        var (preset, shell) = (new AssetPreset { Name = "P", MediaSource = "online" }, new MainShellWindow());
        s.AssetPresets.Add(preset);
        try
        {
            shell.Show(); shell.ShowTab("assets");
            Dispatcher.UIThread.RunJobs();
            var chip = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.AssetsTabView>("AssetsTab")!
                .GetLogicalDescendants().OfType<global::Avalonia.Controls.Primitives.ToggleButton>().Single(b => b.Name == "ChipSource_online");
            Assert.False(chip.IsChecked);
            var dlg = new LauncherMediaDialog { Panel = shell };
            dlg.GetLogicalDescendants().OfType<ComboBox>().Single().SelectedItem = s.AssetPresets.Single(p => p.Id == preset.Id);
            Assert.True(s.MediaSource == "online" && s.CurrentAssetPresetId == preset.Id);
            Assert.True(chip.IsChecked);   // the panel's page repainted: it applied the preset, not the dialog alone
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void DesktopEntryExec_EscapesTwice_PerTheSpec() =>
        Assert.Equal("\"/o p/a\\\\\\\\b\\\\\"c\\\\$d\\\\`e%%\"", XdgAutostart.Quote("/o p/a\\b\"c$d`e%"));

    [Fact]
    public void LinuxEntry_LandsInTheApplicationsFolder_AndOnTheDesktop()
    {
        if (OperatingSystem.IsWindows()) return;   // the Windows branch writes a .lnk through COM
        var root = Path.Combine(Path.GetTempPath(), "ccp-shortcut-" + Guid.NewGuid().ToString("N"));
        var (apps, desktop) = (Path.Combine(root, "apps"), Directory.CreateDirectory(Path.Combine(root, "desk")).FullName);
        try
        {
            (LauncherShortcutWriter.ApplicationsOverride, LauncherShortcutWriter.DesktopOverride) = (apps, desktop);
            Assert.True(LauncherShortcutWriter.TryCreateDesktopShortcut("dtrh"));
            foreach (var folder in new[] { apps, desktop })
            {
                var path = Path.Combine(folder, "cclabs-dtrh.desktop");
                Assert.Contains(" --game dtrh\n", File.ReadAllText(path));
                Assert.True(File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute));
            }
            Assert.True(LauncherShortcutWriter.TryCreateDesktopShortcut(null));
            Assert.Contains(" --panel\n", File.ReadAllText(Path.Combine(apps, "cclabs-panel.desktop")));
            Assert.False(LauncherShortcutWriter.TryCreateDesktopShortcut("no-such-game"));

            // Without a folder named by the test, a test host writes nothing anywhere.
            (LauncherShortcutWriter.ApplicationsOverride, LauncherShortcutWriter.DesktopOverride) = (null, null);
            Assert.False(LauncherShortcutWriter.TryCreateDesktopShortcut(null));
        }
        finally
        {
            (LauncherShortcutWriter.ApplicationsOverride, LauncherShortcutWriter.DesktopOverride) = (null, null);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void MediaDialog_AsksBeforeGoingOnline_KeepsASource_AndTheLastNiche() => Run(() =>
    {
        var s = CoreSettings.Current;
        (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.BackRoomMediaSource) = ("local", false, false, "bundled");
        var dlg = new LauncherMediaDialog();
        var (online, local) = (dlg.SourceChips.First(c => Equals(c.Tag, "online")), dlg.SourceChips.First(c => Equals(c.Tag, "local")));
        Assert.True(local.IsChecked == true && !dlg.RatioRowVisible);
        Assert.False(dlg.CreditLine.IsVisible);   // WPF LauncherMediaDialog.xaml.cs:139: no Scrolller credit on local media

        dlg.ConsentOverride = (_, _) => Task.FromResult(false);   // "No" writes nothing
        online.IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.True(s.MediaSource == "local" && !s.HasRemoteMediaConsent && local.IsChecked == true && online.IsChecked == false);

        dlg.ConsentOverride = (_, _) => Task.FromResult(true);    // "Yes" is remembered; the room follows
        online.IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.True(s.MediaSource == "online" && s.HasRemoteMediaConsent && s.BackRoomMediaSource == "auto");
        Assert.True(dlg.CreditLine.IsVisible);    // "Media from Scrolller" while online is in play

        online.IsChecked = false;                                  // never no source at all
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.True(s.MediaSource == "online" && online.IsChecked == true);

        foreach (var chip in dlg.NicheChips) chip.IsChecked = false;
        Assert.True(dlg.NicheChips.Count(c => c.IsChecked == true) == 1 && s.FypOnlineNiches.Count == 1);

        dlg.SourceChips.First(c => Equals(c.Tag, "mixed")).IsChecked = true;
        dlg.LastChange.GetAwaiter().GetResult();
        Assert.True(dlg.RatioRowVisible);
        dlg.MasterSlider.Value = 40;
        Assert.Equal(40, s.MasterVolume);
    });
}
