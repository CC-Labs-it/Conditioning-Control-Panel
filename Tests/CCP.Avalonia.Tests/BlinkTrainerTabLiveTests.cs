using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Blink Trainer page's camera-free half (WPF MainWindow.BlinkTrainer.cs +
/// TabNavigation RefreshBlinkTrainerTab): saved settings survive the load pass, the free-tier gate
/// is up over a running demo stage, and the status row follows consent and folders.</summary>
public sealed class BlinkTrainerTabLiveTests
{
    [Fact]
    public void FolderCardsAreReusedUntilInputsChange() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var folders = settings.BlinkTrainerFolders.ToArray();
        var videos = settings.BlinkTrainerIncludeVideos;
        try
        {
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.Add("/missing-folder");
            settings.BlinkTrainerFolders.Add("/missing-folder");
            var tab = new BlinkTrainerTabView();
            tab.Refresh();
            var host = tab.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!;
            Assert.Equal(2, host.Children.Count);
            var first = host.Children[0];
            tab.Refresh();
            Assert.Same(first, host.Children[0]);
            settings.BlinkTrainerIncludeVideos = !settings.BlinkTrainerIncludeVideos;
            tab.Refresh();
            Assert.NotSame(first, host.Children[0]);
            first = host.Children[0];
            tab.Resources["PinkBrush"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Blue);
            tab.Refresh();
            Assert.NotSame(first, host.Children[0]);
            first = host.Children[0];
            settings.BlinkTrainerFolders.RemoveAt(0);
            tab.Refresh();
            Assert.Single(host.Children);
            Assert.NotSame(first, host.Children[0]);
        }
        finally
        {
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.AddRange(folders);
            settings.BlinkTrainerIncludeVideos = videos;
        }
    });

    [Theory]
    [InlineData("folders")]
    [InlineData("videos")]
    [InlineData("language")]
    [InlineData("pink")]
    public void FolderCardInputsInvalidateReuse(string input) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var oldFolders = settings.BlinkTrainerFolders.ToArray();
        var oldVideos = settings.BlinkTrainerIncludeVideos;
        var localization = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var oldLanguage = localization.CurrentLanguage;
        try
        {
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.Add("/missing-folder");
            var tab = new BlinkTrainerTabView();
            tab.Refresh();
            var host = tab.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!;
            var first = Assert.Single(host.Children);
            switch (input)
            {
                case "folders": settings.BlinkTrainerFolders.Add("/another-missing-folder"); break;
                case "videos": settings.BlinkTrainerIncludeVideos = !oldVideos; break;
                case "language": localization.SetLanguage(oldLanguage == "en" ? "fr" : "en"); break;
                case "pink": tab.Resources["PinkBrush"] = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Colors.Blue); break;
            }
            tab.Refresh();
            Assert.NotSame(first, host.Children[0]);
        }
        finally
        {
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.AddRange(oldFolders);
            settings.BlinkTrainerIncludeVideos = oldVideos;
            if (localization.CurrentLanguage != oldLanguage) localization.SetLanguage(oldLanguage);
        }
    });

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("remove-folder")]
    [InlineData("restore-folder")]
    public void ReturningToTabRefreshesFolderCounts(string change) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var oldFolders = settings.BlinkTrainerFolders.ToArray();
        var oldVideos = settings.BlinkTrainerIncludeVideos;
        var dir = Directory.CreateTempSubdirectory("ccp-blink-count-").FullName;
        var file = Path.Combine(dir, "a.png");
        Window? window = null;
        try
        {
            if (change != "add" && change != "restore-folder") File.WriteAllBytes(file, Array.Empty<byte>());
            if (change == "restore-folder") Directory.Delete(dir);
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.Add(dir);
            settings.BlinkTrainerIncludeVideos = false;
            var tab = new BlinkTrainerTabView { IsVisible = false };
            tab.Resources["TextMutedBrush"] = global::Avalonia.Media.Brushes.White;
            tab.Resources["TextDimBrush"] = global::Avalonia.Media.Brushes.Gray;
            window = new Window { Content = tab };
            window.Show();
            tab.IsVisible = true;
            var host = tab.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!;
            var card = Assert.IsType<Border>(Assert.Single(host.Children));
            var info = Assert.IsType<StackPanel>(Assert.IsType<Grid>(card.Child).Children[0]);
            var count = Assert.IsType<TextBlock>(info.Children[1]);
            tab.IsVisible = false;
            switch (change)
            {
                case "add": File.WriteAllBytes(file, Array.Empty<byte>()); break;
                case "delete": File.Delete(file); break;
                case "remove-folder": Directory.Delete(dir, true); break;
                case "restore-folder": Directory.CreateDirectory(dir); File.WriteAllBytes(file, Array.Empty<byte>()); break;
            }
            tab.IsVisible = true;
            Assert.Same(card, Assert.Single(host.Children));
            bool valid = change == "add" || change == "restore-folder";
            Assert.Equal(valid ? "1 images" :
                ConditioningControlPanel.Localization.Loc.Get("blink_trainer_folder_empty_or_invalid"), count.Text);
            Assert.Same(valid ? global::Avalonia.Media.Brushes.White : global::Avalonia.Media.Brushes.Gray, count.Foreground);
        }
        finally
        {
            window?.Close();
            settings.BlinkTrainerFolders.Clear();
            settings.BlinkTrainerFolders.AddRange(oldFolders);
            settings.BlinkTrainerIncludeVideos = oldVideos;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void DirectStartHandlerDemandsPremiumBeforeOpeningTheCamera() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        var oldDenied = ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler;
        var denied = 0;
        ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => false;
        ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler = _ => denied++;
        try
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            new BlinkTrainerTabView().BtnBlinkTrainerStartSession_Click(null, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.Equal(1, denied);
            Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BlinkTrainerSession.IsRunning);
        }
        finally
        {
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = old;
            ConditioningControlPanel.CoreEntitlement.ShowDeniedHandler = oldDenied;
        }
    });

    /// <summary>A refresh keeps the bound consent and status-action labels while their keys hold
    /// (restack b38, from #3670): the bindings follow language changes, nothing re-templates.</summary>
    [Fact]
    public void RefreshKeepsTheBoundActionLabels() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tab = new BlinkTrainerTabView();
        tab.Refresh();
        var consent = tab.FindControl<Button>("BtnBlinkTrainerManageConsent")!;
        var action = tab.FindControl<Button>("BlinkTrainerStatusAction")!;
        var (consentLabel, actionLabel) = (consent.Content, action.Content);
        Assert.NotNull(consentLabel);
        tab.Refresh();
        Assert.Same(consentLabel, consent.Content);
        Assert.Same(actionLabel, action.Content);
    });

    [Fact]
    public void ShowingTheTab_LoadsSettings_GatesAndDemos_AndStatusFollowsFolders()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var s = ConditioningControlPanel.CoreSettings.Current;
            var old = (s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, s.BlinkTrainerMixImages,
                s.WebcamConsentGiven, s.WebcamConsentVersion, s.BlinkTrainerFolders.ToArray());
            var dir = Directory.CreateTempSubdirectory("ccp-blink-").FullName;
            File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[] { 1 });
            Window? host = null;
            var oldPremium = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = null; // free tier, whatever ran before
            try
            {
                if (Application.Current is null)
                    global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();

                s.BlinkTrainerDurationMinutes = 42;
                s.BlinkTrainerOpacity = 33;
                s.BlinkTrainerMixImages = true;
                s.WebcamConsentGiven = true;
                s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                s.BlinkTrainerFolders.Clear();

                var tab = new BlinkTrainerTabView { IsVisible = false };
                host = new Window { Width = 1400, Height = 1000, Content = tab };
                host.Show();
                tab.IsVisible = true; // what ShowTab("blinktrainer") does
                Dispatcher.UIThread.RunJobs();

                // The load pass paints the saved values and does not write the markup defaults back.
                Assert.Equal(42, tab.FindControl<Slider>("SliderBlinkTrainerDurationNew")!.Value);
                Assert.Equal("42 min", tab.FindControl<TextBlock>("TxtBlinkTrainerDurationValue")!.Text);
                Assert.Equal("33%", tab.FindControl<TextBlock>("TxtBlinkTrainerOpacityValue")!.Text);
                Assert.Equal(42, s.BlinkTrainerDurationMinutes);
                Assert.Equal(33, s.BlinkTrainerOpacity);
                Assert.NotEqual(default, tab.FindControl<Border>("BlinkTrainerMixOptionMix")!.BoxShadow);

                // No entitlement on this head: gate up, gated editors off, demo stage cycling.
                Assert.True(tab.FindControl<Border>("BlinkTrainerGate")!.IsVisible);
                Assert.False(tab.FindControl<StackPanel>("BlinkTrainerGatedContent")!.IsEnabled);
                Assert.True(tab.DemoRunning);
                var a = tab.FindControl<Image>("BlinkTrainerStageImageA")!;
                Assert.NotNull(a.Source);
                tab.AdvanceDemo();
                Assert.NotNull(tab.FindControl<Image>("BlinkTrainerStageImageB")!.Source);

                // Consent given, no folders: the row asks for a folder and blocks the start.
                Assert.Equal(BlinkTrainerStatusState.NeedsFolders, tab.StatusState);
                Assert.True(tab.FindControl<Button>("BlinkTrainerStatusAction")!.IsVisible);
                Assert.False(tab.FindControl<Button>("BtnBlinkTrainerStartSession")!.IsEnabled);

                tab.AddFolder(dir);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(BlinkTrainerStatusState.IdleReady, tab.StatusState);
                Assert.Single(tab.FindControl<StackPanel>("BlinkTrainerFolderCardsHost")!.Children);
                Assert.Contains(dir, s.BlinkTrainerFolders);

                // Ready: Start is live (WPF SetStartButtonState), and so is Calibrate (webcam slice 5).
                Assert.True(tab.FindControl<Button>("BtnBlinkTrainerStartSession")!.IsEnabled);
                Assert.True(tab.FindControl<Button>("BtnBlinkTrainerCalibrate")!.IsEnabled);
                // The tracker toggle is live now (Platform/WebcamTracker), labelled as WPF does.
                var tracker = tab.FindControl<Button>("BtnBlinkTrainerStartStopTracker")!;
                Assert.True(tracker.IsEnabled);
                Assert.Equal("Start tracker", ((TextBlock)tracker.Content!).Text);

                tab.IsVisible = false; // leaving the tab stops the loop
                Assert.False(tab.DemoRunning);

                // A patron: gate down, editors live; consent + a folder is live preview, so the
                // demo stops and the stage parks (WPF ResetBlinkTrainerStageForLive).
                ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
                tab.IsVisible = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(tab.FindControl<Border>("BlinkTrainerGate")!.IsVisible);
                Assert.True(tab.FindControl<StackPanel>("BlinkTrainerGatedContent")!.IsEnabled);
                Assert.False(tab.DemoRunning);
                Assert.Equal(0, a.Opacity);

                // Revoke clears the consent record and the row asks for consent again.
                tab.RevokeConsent();
                Assert.False(WebcamConsent.IsCurrent(s));
                Assert.Equal(BlinkTrainerStatusState.NeedsConsent, tab.StatusState);
                Assert.True(tab.DemoRunning);
            }
            finally
            {
                host?.Close();
                ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = oldPremium;
                (s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, s.BlinkTrainerMixImages,
                    s.WebcamConsentGiven, s.WebcamConsentVersion) = (old.Item1, old.Item2, old.Item3, old.Item4, old.Item5);
                s.BlinkTrainerFolders.Clear();
                s.BlinkTrainerFolders.AddRange(old.Item6);
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public void Status_PriorityOrder_MatchesWpf()
    {
        Assert.Equal(BlinkTrainerStatusState.Running, BlinkTrainerState.Status(true, "x", false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.Error, BlinkTrainerState.Status(false, "x", false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsConsent, BlinkTrainerState.Status(false, null, false, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsFolders, BlinkTrainerState.Status(false, null, true, 0, true, false));
        Assert.Equal(BlinkTrainerStatusState.NeedsCalibration, BlinkTrainerState.Status(false, null, true, 1, true, false));
        Assert.Equal(BlinkTrainerStatusState.IdleReady, BlinkTrainerState.Status(false, null, true, 1, false, false));
        Assert.Null(BlinkTrainerState.FolderCountLine(null, false));

        var dir = Directory.CreateTempSubdirectory("ccp-blink-count-").FullName;
        try
        {
            foreach (var f in new[] { "a.png", "b.jpg", "c.gif", "d.mp4" })
                File.WriteAllBytes(Path.Combine(dir, f), new byte[] { 1 });
            var pack = ConditioningControlPanel.Lab.GazeMinigame.AssetPack.FromFolder(dir);
            Assert.Equal("2 images, 1 GIFs", BlinkTrainerState.FolderCountLine(pack, includeVideos: false));
            Assert.Equal("3 images, 1 videos", BlinkTrainerState.FolderCountLine(pack, includeVideos: true));
            File.Delete(Path.Combine(dir, "c.gif"));
            Assert.Equal("2 images", BlinkTrainerState.FolderCountLine(
                ConditioningControlPanel.Lab.GazeMinigame.AssetPack.FromFolder(dir), includeVideos: false));
        }
        finally { Directory.Delete(dir, true); }
    }
}
