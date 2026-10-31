using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class EngineEmptyLibraryTests
{
    [Fact]
    public Task UserStartWarnsWithBothRecoveryActions() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-engine-empty-").FullName;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var oldStop = CoreEngine.StoppedHook;
        var hostField = typeof(NotificationService).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var oldHost = hostField.GetValue(AvApp.Notifications);
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CorePaths.EffectiveAssetsProvider = () => dir;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Off;
        s.FlashEnabled = true; s.MediaSource = "local";
        s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = s.LockCardEnabled = false;
        s.MandatoryVideosEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        var shell = new MainShellWindow();
        var host = shell.Named<StackPanel>("NotificationHost")!;
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            AvApp.Notifications.AttachHost(host);
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartEngine();
            Assert.True(CoreEngine.IsRunning);
            Assert.Empty(host.Children);
            MainShellWindow.StopEngine();
            shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
            var toast = Assert.IsType<Border>(Assert.Single(host.Children));
            Assert.Contains(Loc.Get("tab_assets"), ((TextBlock)((Grid)toast.Child!).Children[0]).Text);
            Assert.Equal(TimeSpan.FromSeconds(18), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
            var buttons = toast.GetVisualDescendants().OfType<Button>().ToArray();
            var packs = buttons.Single(b => b.Name == "ToastSecondaryAction");
            Assert.Equal(Loc.Get("btn_get_packs"), Assert.IsType<TextBlock>(packs.Content).Text);
            buttons.Single(b => b.Name == "ToastAction").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("assets", shell.CurrentTab);
            Assert.False(((DispatcherTimer)toast.Tag!).IsEnabled);
            shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.True(s.FlashEnabled);
        }
        finally
        {
            CoreEngine.Stop(); CoreEngine.StoppedHook = oldStop;
            foreach (var toast in host.Children.OfType<Border>()) (toast.Tag as DispatcherTimer)?.Stop();
            host.Children.Clear(); hostField.SetValue(AvApp.Notifications, oldHost);
            shell.Close(); CoreSettings.ServiceProvider = oldSettings; CorePaths.EffectiveAssetsProvider = oldAssets;
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    });
}
