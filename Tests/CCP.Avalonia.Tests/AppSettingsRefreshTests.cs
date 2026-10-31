using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AppSettingsRefreshTests
{
    [Fact]
    public void ReopeningSettingsRefreshesAttachedSectionsAndHotpluggedCameras()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var root = Directory.CreateTempSubdirectory("ccp-settings-refresh-").FullName;
            var oldCameras = V4l2Cameras.Root;
            var oldCameraProvider = CameraList.Provider;
            var oldStartup = XdgAutostart.DirectoryOverride;
            var oldWinStartup = WindowsStartupShortcut.DirectoryOverride;
            var oldAvailable = CoreWebcam.IsAvailableProvider;
            var oldMics = CoreSpeech.EnumerateInputDevicesProvider;
            var mics = new[] { new SpeechInputDevice(-1, "System default") };
            var s = CoreSettings.Current;
            var (oldRun, oldVideo, oldPerf, oldSuppress) =
                (s.RunOnStartup, s.StartupVideoPath, s.PerformanceMode, s.SuppressPerkNotifications);
            MainShellWindow? shell = null;
            try
            {
                V4l2Cameras.Root = root;
                CameraList.Provider = V4l2Cameras.Enumerate; // Use the synthetic tree on Windows too.
                XdgAutostart.DirectoryOverride = Path.Combine(root, "autostart");
                WindowsStartupShortcut.DirectoryOverride = Path.Combine(root, "startup");   // Windows reads a Startup .lnk (port/7.2)
                CoreWebcam.IsAvailableProvider = () => true;
                CoreSpeech.EnumerateInputDevicesProvider = () => mics;
                s.RunOnStartup = false;
                s.StartupVideoPath = "old.mp4";
                s.PerformanceMode = false;
                s.SuppressPerkNotifications = false;
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<AppSettingsTabView>("AppSettingsTab")!;
                var general = page.FindControl<GeneralSettingsSection>("SectionGeneral")!;
                var devices = page.FindControl<DevicesSettingsSection>("SectionDevices")!;
                var performance = page.FindControl<PerformanceSettingsSection>("SectionPerformance")!;
                var notifications = page.FindControl<NotificationsSettingsSection>("SectionNotifications")!;
                var cameras = devices.FindControl<ComboBox>("CmbWebcamDevice")!;
                Assert.Equal("old.mp4", general.FindControl<TextBlock>("TxtStartupVideo")!.Text);
                Assert.DoesNotContain(cameras.Items.Cast<ComboBoxItem>(), i => Equals(i.Content, "[4] New camera"));

                // Hiding a tab does not detach its controls. These changes happen while it is hidden.
                shell.ShowTab("settings");
                s.StartupVideoPath = "new.mp4";
                s.PerformanceMode = true;
                s.SuppressPerkNotifications = true;
                mics = new[] { new SpeechInputDevice(-1, "System default"), new SpeechInputDevice(7, "New microphone") };
                Assert.True(SessionAutostart.SetStartupState(true));   // the platform's own registration
                var camera = Directory.CreateDirectory(Path.Combine(root, "video4")).FullName;
                File.WriteAllText(Path.Combine(camera, "name"), "New camera\n");
                File.WriteAllText(Path.Combine(camera, "index"), "0\n");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("old.mp4", general.FindControl<TextBlock>("TxtStartupVideo")!.Text);

                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                Assert.Same(page, shell.Named<AppSettingsTabView>("AppSettingsTab"));
                Assert.Equal("new.mp4", general.FindControl<TextBlock>("TxtStartupVideo")!.Text);
                Assert.True(general.FindControl<CheckBox>("ChkWinStart")!.IsChecked);
                Assert.True(s.RunOnStartup); // adopts external registration, as WPF does
                Assert.True(performance.FindControl<CheckBox>("ChkPerformanceMode")!.IsChecked);
                Assert.True(notifications.FindControl<CheckBox>("ChkSuppressPerkNotifications")!.IsChecked);
                Assert.Contains(cameras.Items.Cast<ComboBoxItem>(), i => Equals(i.Content, "[4] New camera"));
                Assert.Contains(devices.FindControl<ComboBox>("CmbMicDevice")!.Items.Cast<ComboBoxItem>(),
                    i => Equals(i.Content, "New microphone"));
                Assert.True(s.PerformanceMode);
                Assert.True(s.SuppressPerkNotifications); // repaint did not overwrite either edit
            }
            finally
            {
                shell?.Close();
                V4l2Cameras.Root = oldCameras;
                CameraList.Provider = oldCameraProvider;
                XdgAutostart.DirectoryOverride = oldStartup;
                WindowsStartupShortcut.DirectoryOverride = oldWinStartup;
                CoreWebcam.IsAvailableProvider = oldAvailable;
                CoreSpeech.EnumerateInputDevicesProvider = oldMics;
                (s.RunOnStartup, s.StartupVideoPath, s.PerformanceMode, s.SuppressPerkNotifications) =
                    (oldRun, oldVideo, oldPerf, oldSuppress);
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void EveryOpenRefreshesSectionsAndOneFailureDoesNotStopTheRest()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldStartup = XdgAutostart.DirectoryOverride;
            var root = Directory.CreateTempSubdirectory("ccp-settings-probes-").FullName;
            XdgAutostart.DirectoryOverride = root;
            try
            {
                var page = new AppSettingsTabView();
                var stack = page.FindControl<StackPanel>("SectionStack")!;
                stack.Children.Clear();
                var first = new RefreshProbe(throws: true);
                var second = new RefreshProbe(throws: false);
                stack.Children.Add(first);
                stack.Children.Add(new Border()); // optional interface: static sections are skipped
                stack.Children.Add(second);
                page.RefreshSections();
                page.RefreshSections();
                Assert.Equal(2, first.Calls);
                Assert.Equal(2, second.Calls);
            }
            finally
            {
                XdgAutostart.DirectoryOverride = oldStartup;
                Directory.Delete(root, true);
            }
        });
    }

    private sealed class RefreshProbe(bool throws) : UserControl, IAppSettingsSection
    {
        public int Calls { get; private set; }
        public void OnSectionShown()
        {
            Calls++;
            if (throws) throw new InvalidOperationException("refresh failed");
        }
    }
}
