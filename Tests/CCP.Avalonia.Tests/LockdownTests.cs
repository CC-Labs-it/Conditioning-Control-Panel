using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The three Lockdown shipping blockers (docs/avalonia-decisions.html, Lockdown / Emergency
/// Exit): the phrase path works under Strict Lock through the real controls, keys are ignored only at
/// the global layer, and a killed run gets its panic key and Strict Lock back.</summary>
public sealed class LockdownTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task LockdownArtworkUsesTheShippedSideHeroAndEmergencyPlates()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var tab = new LockdownTabView();
            var side = tab.FindControl<Image>("ImgLockdownFeature");
            Assert.NotNull(side);
            Assert.NotNull(side.Source);
            Assert.True(ConditioningControlPanel.Avalonia.Controls.HoverPop.GetIsEnabled(side));
            Assert.Equal(new Thickness(10), side.Margin);
            var hero = Assert.IsType<global::Avalonia.Media.ImageBrush>(
                tab.FindControl<Border>("LockdownHeroArt")!.Background);
            Assert.Same(side.Source, hero.Source);
            Assert.NotNull(Assert.IsType<global::Avalonia.Media.ImageBrush>(
                tab.FindControl<Border>("EEFacePlate")!.Background).Source);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ArtworkTracksModSwitchesOnlyWhileAttachedAndRefreshesOnReattach()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var previous = CoreModArt.OverridePathProvider;
            var tab = new LockdownTabView();
            var win = new Window { Content = tab };
            var png = Path.GetTempFileName();
            using (var source = global::Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://CCP.Avalonia/Resources/lockdown/emergency_exit.png")))
            using (var target = File.Create(png)) source.CopyTo(target);
            try
            {
                win.Show();
                var side = tab.FindControl<Image>("ImgLockdownFeature")!;
                var before = side.Source;
                var requested = false;
                CoreModArt.OverridePathProvider = path =>
                {
                    if (path == "lockdown_icon.png") { requested = true; return png; }
                    throw new InvalidOperationException("Emergency Exit must use built-in art");
                };
                void Switch() => CoreMods.RaiseModChanged(null,
                    new ConditioningControlPanel.Models.ModPackage(new ConditioningControlPanel.Models.ModManifest(), null, false));
                Switch();
                Dispatcher.UIThread.RunJobs();
                Assert.True(requested);
                Assert.NotSame(before, side.Source);
                Assert.Same(side.Source, Assert.IsType<global::Avalonia.Media.ImageBrush>(
                    tab.FindControl<Border>("LockdownHeroArt")!.Background).Source);
                using var expected = new global::Avalonia.Media.Imaging.Bitmap(png);
                Assert.Equal(expected.PixelSize, Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(side.Source).PixelSize);
                win.Content = null;
                requested = false;
                Switch();
                Dispatcher.UIThread.RunJobs();
                Assert.False(requested);
                CoreModArt.OverridePathProvider = null;
                win.Content = tab;
                Assert.NotSame(before, side.Source);
                Assert.NotEqual(expected.PixelSize, Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(side.Source).PixelSize);
            }
            finally { win.Close(); CoreModArt.OverridePathProvider = previous; File.Delete(png); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EmergencyExitPressUsesSixtyMsOutAndOneFortyMsHome()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            using var ld = LockdownService.Current = new LockdownService();
            var tab = new LockdownTabView();
            var win = new Window { Content = tab, Width = 1200, Height = 1000 };
            win.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(30));
                Dispatcher.UIThread.RunJobs();
                tab.BtnEmergencyExit.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var root = tab.FindControl<Grid>("EERoot")!;
                var p = tab.BtnEmergencyExit.TranslatePoint(new Point(40, 40), win)!.Value;
                win.MouseDown(p, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var down = Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(root.Transitions!));
                Assert.Equal(TimeSpan.FromMilliseconds(60), down.Duration);
                Assert.IsType<global::Avalonia.Animation.Easings.QuadraticEaseOut>(down.Easing);
                win.MouseUp(p, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var up = Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(root.Transitions!));
                Assert.Equal(TimeSpan.FromMilliseconds(140), up.Duration);
                Assert.IsType<global::Avalonia.Animation.Easings.QuadraticEaseInOut>(up.Easing);
            }
            finally
            {
                ld.Deactivate();
                LockdownService.Current = null;
                win.Close();
            }
            return Task.CompletedTask;
        });
    }

    private static void Arm(bool forceStrict, bool disablePanic)
    {
        var s = CoreSettings.Current;
        s.PanicKeyEnabled = true;
        s.PanicKey = "F8";
        s.StrictLockEnabled = false;
        s.LockdownForceStrictLock = forceStrict;
        s.LockdownDisablePanicKey = disablePanic;
    }

    [Fact]
    public async Task PhraseTypedThroughTheRealControlsEndsALockdownUnderStrictLock()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: true, disablePanic: true);
            var ld = LockdownService.Current = new LockdownService();
            var tab = new LockdownTabView();
            var win = new Window { Content = tab, Width = 1200, Height = 1000 };
            win.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(30));
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreSettings.Current.StrictLockEnabled);
                Assert.True(tab.LockdownActivePanel.IsVisible);
                Assert.False(tab.LockdownSetupPanel.IsVisible);

                // The slab: a notice with the steps, never the box, never an end.
                tab.BtnEmergencyExit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(tab.TxtEmergencyExitNotice.IsVisible);
                Assert.Contains("let me out", tab.TxtEmergencyExitNotice.Text);
                Assert.False(tab.TxtLockdownExit.IsVisible);
                Assert.True(ld.IsActive);

                void FiveTaps()
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last frame
                    Dispatcher.UIThread.RunJobs();
                    var p = tab.TxtLockdownTimer.TranslatePoint(
                        new Point(20, tab.TxtLockdownTimer.Bounds.Height / 2), win)!.Value;   // on the digits
                    for (var i = 0; i < 5; i++)
                    {
                        win.MouseDown(p, MouseButton.Left);
                        win.MouseUp(p, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                    }
                }
                void Type(string text)
                {
                    tab.TxtLockdownExit.Focus();
                    win.KeyTextInput(text);
                    win.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                FiveTaps();
                Assert.True(tab.TxtLockdownExit.IsVisible);
                Type("let me in");                      // wrong: box cleared + hidden, still locked
                Assert.False(tab.TxtLockdownExit.IsVisible);
                Assert.True(ld.IsActive);

                FiveTaps();
                Type("let me out");
                Dispatcher.UIThread.RunJobs();
                Assert.False(ld.IsActive);
                Assert.False(CoreSettings.Current.StrictLockEnabled);   // the pre-lockdown value
                Assert.True(CoreSettings.Current.PanicKeyEnabled);
                Assert.True(tab.LockdownSetupPanel.IsVisible);
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                win.Close();
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Activate with the shell hidden (tray / behind the launcher): Avalonia's ShowDialog
    /// threw "Cannot show window with non-visible owner"; WPF shows the consent anyway, so the
    /// dialog opens ownerless and its answer still activates.</summary>
    [Fact]
    public async Task ActivateWithTheShellHiddenShowsTheConsentAndActivates()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: false, disablePanic: false);
            var oldPremium = CoreEntitlement.HasPremiumProvider;
            CoreEntitlement.HasPremiumProvider = () => true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            WarningDialog? shown = null;
            void OnOpened(object? s, RoutedEventArgs e) => shown ??= s as WarningDialog;
            using var hook = Window.WindowOpenedEvent.AddClassHandler<WarningDialog>(OnOpened);
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                shell.Hide();
                var tab = shell.Named<LockdownTabView>("LockdownTab")!;
                tab.CmbLockdownDuration.SelectedIndex = 0;
                tab.BtnActivateLockdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(shown);
                Assert.True(shown!.IsVisible);
                shown.FindControl<CheckBox>("ChkConfirm")!.IsChecked = true;
                shown.FindControl<Button>("BtnConfirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(ld.IsActive);
            }
            finally
            {
                shown?.Close();
                CoreEntitlement.HasPremiumProvider = oldPremium;
                ld.Dispose();
                LockdownService.Current = null;
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task GlobalPanicStopAndExitAreRefusedUnderLockdownAndWorkAfter()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: false, disablePanic: false);   // panic flag stays on: only Lockdown refuses
            var s = CoreSettings.Current;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                s.FlashEnabled = true;
                shell.StartEngine();
                ld.Activate(TimeSpan.FromMinutes(30));
                Assert.True(s.PanicKeyEnabled);

                shell.HandlePanicKeyPress(t0);
                shell.HandlePanicKeyPress(t0.AddSeconds(0.5));   // a double press would exit
                MainShellWindow.StopEverything();                // tray panic
                shell.RequestExit();
                shell.Close();                                   // the X
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning);
                Assert.False(closed);

                // A TextBox in the same window still takes keys: only the global layer is ignored.
                var box = new TextBox();
                shell.Content = box;
                Dispatcher.UIThread.RunJobs();
                box.Focus();
                shell.KeyTextInput("typed");
                Assert.Equal("typed", box.Text);

                ld.Deactivate();
                shell.HandlePanicKeyPress(t0.AddSeconds(10));
                Assert.False(CoreEngine.IsRunning);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                ld.Dispose();
                LockdownService.Current = null;
                s.FlashEnabled = false;
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task APresetLoadedMidLockdownCannotSwitchStrictLockOff()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: true, disablePanic: false);
            var s = CoreSettings.Current;
            s.BubbleCountStrictLock = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var tab = shell.Named<PresetsTabView>("PresetsTab")!;
                var loose = ConditioningControlPanel.Models.Preset.FromSettings(s, "Loose");
                loose.StrictLockEnabled = loose.BubbleCountStrictLock = false;
                ld.Activate(TimeSpan.FromMinutes(30));
                Assert.True(tab.LoadPreset(loose));
                Assert.True(s.StrictLockEnabled && s.BubbleCountStrictLock);   // WPF LockdownStrictHold.Keep
                ld.Deactivate();
                Assert.True(tab.LoadPreset(loose));
                Assert.False(s.BubbleCountStrictLock);                         // no lockdown: the preset wins
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                s.BubbleCountStrictLock = false;
                shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TheSlabTripsTheWireHoldsChasterAndShowsTheRealTimeEvenWhenTheTimerIsHidden()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            Arm(forceStrict: true, disablePanic: true);
            CoreSettings.Current.HideLockdownTimer = true;
            var ld = LockdownService.Current = new LockdownService();
            var chaster = ConditioningControlPanel.Avalonia.Platform.ChasterHead.Service =
                ConditioningControlPanel.Avalonia.Platform.ChasterHead.Create("sandbox", null);
            var holds = 0;
            chaster.LockChanged += () => holds++;
            string? kind = null;
            ld.EscapeAttempted += a => kind = a.Kind;
            var tab = new LockdownTabView();
            var win = new Window { Content = tab };
            win.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(30));
                Dispatcher.UIThread.RunJobs();
                tab.BtnEmergencyExit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ConditioningControlPanel.Services.Possession.EscapeKinds.EmergencyExit, kind);
                Assert.Equal(1, holds);
                Assert.Contains("29:5", tab.TxtEmergencyExitNotice.Text);   // 29:5x, never "••:••"
                Assert.DoesNotContain(SessionClockLabel.HiddenLockdownClock, tab.TxtEmergencyExitNotice.Text);
                Assert.True(ld.IsActive);
            }
            finally
            {
                ld.Dispose();
                LockdownService.Current = null;
                ConditioningControlPanel.Avalonia.Platform.ChasterHead.Service = null;
                CoreSettings.Current.HideLockdownTimer = false;
                win.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void RemainingFollowsAMonotonicUtcClockNotTheWallClock()
    {
        Arm(forceStrict: false, disablePanic: false);
        var now = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc);   // the EU fall-back night
        using var ld = new LockdownService { UtcNow = () => now };
        ld.Activate(TimeSpan.FromMinutes(30));
        now = now.AddMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(20), ld.Remaining);   // local time went back an hour; UTC did not
        ld.Deactivate();
        Assert.Equal(TimeSpan.FromMinutes(10), ld.LastActiveDuration);
    }

    [Fact]
    public async Task TheTimerRunningOutEndsTheLockdown()
    {
        Arm(forceStrict: true, disablePanic: true);
        var now = DateTime.UtcNow;
        using var ld = new LockdownService { UtcNow = () => now };
        ld.Activate(TimeSpan.FromMinutes(5));
        now = now.AddMinutes(6);
        for (var i = 0; i < 40 && ld.IsActive; i++) await Task.Delay(100);   // the 1 s countdown tick
        Assert.False(ld.IsActive);
        Assert.True(CoreSettings.Current.PanicKeyEnabled);
        Assert.False(CoreSettings.Current.StrictLockEnabled);
    }

    [Fact]
    public void AKilledLockdownGetsThePanicKeyAndStrictLockBackOnTheNextStart()
    {
        Arm(forceStrict: true, disablePanic: true);
        var s = CoreSettings.Current;
        var killed = new LockdownService();
        killed.Activate(TimeSpan.FromMinutes(30));
        Assert.False(s.PanicKeyEnabled);
        Assert.True(s.StrictLockEnabled);
        Assert.True(File.Exists(Path.Combine(CorePaths.UserData, "lockdown_recovery.json")));

        // The process dies here: no Deactivate. The next start runs the recovery first.
        LockdownService.RecoverIfNeeded();
        Assert.True(s.PanicKeyEnabled);
        Assert.False(s.StrictLockEnabled);
        Assert.False(File.Exists(Path.Combine(CorePaths.UserData, "lockdown_recovery.json")));
        killed.Dispose();
    }

    /// <summary>WPF 605c451ae: an empty or broken recovery file (a kill mid-write) still means a lockdown was
    /// interrupted, and the settings on disk may carry its panic-off. It fails OPEN (LockdownExitRule.UnreadableRecovery):
    /// panic on, Strict Lock off. It used to restore nothing, leaving the panic key off for good.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{\"StrictLockEnabled\":tr")]
    [InlineData("null")]
    public void AnUnreadableRecoveryFile_FailsOpen(string content)
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.StrictLockEnabled) = (false, true);   // a lockdown's own values, left on disk
        var path = Path.Combine(CorePaths.UserData, "lockdown_recovery.json");
        Directory.CreateDirectory(CorePaths.UserData);
        File.WriteAllText(path, content);
        LockdownService.RecoverIfNeeded();
        Assert.True(s.PanicKeyEnabled);
        Assert.False(s.StrictLockEnabled);
        Assert.False(File.Exists(path));
    }

    /// <summary>WPF 605c451ae LockdownExitRule.PhraseMatches: the same words typed full-width, with a doubled space
    /// or a touch keyboard's period still open the lockdown; other words never do.</summary>
    [Theory]
    [InlineData("let me out", true)]
    [InlineData("ｌｅｔ　ｍｅ　ｏｕｔ", true)]
    [InlineData("Let  me out.", true)]
    [InlineData("let me in", false)]
    [InlineData("please let me out", false)]
    public void TheExitPhrase_IsForgivingAboutTyping_NeverAboutWords(string typed, bool opens)
    {
        Arm(forceStrict: true, disablePanic: true);
        using var ld = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        Assert.Equal(opens, ld.TryExitWithPhrase(typed));
        Assert.Equal(!opens, ld.IsActive);
        if (ld.IsActive) ld.Deactivate();
        Assert.True(CoreSettings.Current.PanicKeyEnabled);
    }
}
