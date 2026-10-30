using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// dialogs-welcome-back-sheet: WPF's trigger (App.xaml.cs:4626-4760, backup-less on this head) and the
/// sheet's states (WPF WelcomeBackSheetRenderTests). feat-dashboard-logo: the animated logo rides the
/// 30 fps beat on WPF's drive/clock (AnimatedLogoImage.cs:29-35) and parks when hidden or minimised.
/// </summary>
public sealed class WelcomeBackLogoTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    // ---- the trigger ---------------------------------------------------------------------------

    [Fact]
    public async Task TheSheetOffersOnceToAReturningAccountOnAFreshSettingsFile()
    {
        var s = CoreSettings.Current;
        var (level, id, nameProvider) = (s.PlayerLevel, CoreAccount.UnifiedUserId, CoreAccount.DisplayNameProvider);
        var dir = Directory.CreateTempSubdirectory("wb-").FullName;
        var shown = new List<WelcomeBackSheetContent>();
        try
        {
            void Reset()
            {
                WelcomeBackOffer.ResetForTests();
                WelcomeBackOffer.Present = c => { shown.Add(c); return Task.CompletedTask; };
            }

            // Not a fresh device: never armed.
            Reset();
            (s.PlayerLevel, CoreAccount.UnifiedUserId) = (24, "u1");
            CoreAccount.DisplayNameProvider = () => "Bambi";   // WPF App.UserDisplayName, with its provider fallbacks
            WelcomeBackOffer.Arm(settingsFileWasMissing: false, dir);
            await WelcomeBackOffer.TryOfferAsync("startup");
            Assert.Empty(shown);

            // A factory reset: the marker is consumed and nothing is offered.
            Reset();
            var marker = Path.Combine(dir, "settings.json.factory-reset");
            File.WriteAllText(marker, "x");
            WelcomeBackOffer.Arm(true, dir);
            await WelcomeBackOffer.TryOfferAsync("startup");
            Assert.Empty(shown);
            Assert.False(File.Exists(marker));

            // No account, or an account with nothing to welcome back to (level < 2): nothing.
            Reset();
            WelcomeBackOffer.Arm(true, dir);
            CoreAccount.UnifiedUserId = null;
            await WelcomeBackOffer.TryOfferAsync("startup");
            (CoreAccount.UnifiedUserId, s.PlayerLevel) = ("u1", 1);
            await WelcomeBackOffer.TryOfferAsync("startup");
            Assert.Empty(shown);

            // Level 2 with an identity: one greeting, no restore/flavour rows (no backup on this head).
            s.PlayerLevel = 2;
            await WelcomeBackOffer.TryOfferAsync("startup");
            await WelcomeBackOffer.TryOfferAsync("profile loaded");
            var c = Assert.Single(shown);
            Assert.Equal(("Bambi", 2), (c.DisplayName, c.Level));
            Assert.True(c.Plan.ShowSheet);
            Assert.False(c.Plan.ShowRestoreRow || c.Plan.ShowFlavourRow);

            // The second look: the profile loading (sign-in after startup) offers it too.
            shown.Clear();
            Reset();
            WelcomeBackOffer.Arm(true, dir);
            var loaded = (MulticastDelegate?)typeof(AccountSeed)
                .GetField("ProfileLoaded", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            Assert.NotNull(loaded);
            loaded!.DynamicInvoke();
            Assert.Single(shown);
        }
        finally
        {
            WelcomeBackOffer.ResetForTests();
            (s.PlayerLevel, CoreAccount.UnifiedUserId, CoreAccount.DisplayNameProvider) = (level, id, nameProvider);
            Directory.Delete(dir, true);
        }
    }

    // ---- the sheet -----------------------------------------------------------------------------

    [Fact]
    public Task TheSheetsRowsAndChoicesFollowThePlan() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        WelcomeBackSheet Make(WelcomeBackPlan plan, string notes = "", string name = "Bambi") => new(new WelcomeBackSheetContent
        {
            DisplayName = name, Level = 24, BackupModName = "Circe", Plan = plan, VersionLabel = "7.1.5", PatchNotes = notes,
        });
        T F<T>(WelcomeBackSheet w, string n) where T : Control => w.FindControl<T>(n)!;

        var both = Make(new WelcomeBackPlan(true, true, true, "circe"), notes: "notes");
        Assert.True(F<Border>(both, "RowRestore").IsVisible && F<Border>(both, "RowFlavour").IsVisible);
        Assert.True(F<CheckBox>(both, "ChkRestore").IsChecked == true && F<CheckBox>(both, "ChkFlavour").IsChecked == true);
        Assert.Equal("Welcome back, Bambi.", F<TextBlock>(both, "TxtHeading").Text);
        Assert.Contains("24", F<TextBlock>(both, "TxtSubline").Text);
        Assert.Contains("Circe", F<TextBlock>(both, "TxtSubline").Text);
        Assert.True(F<StackPanel>(both, "RowWhatsNew").IsVisible);
        Assert.False(F<Border>(both, "NotesPanel").IsVisible);   // folded away
        F<CheckBox>(both, "ChkFlavour").IsChecked = false;
        both.Commit();
        Assert.True(both.RestoreChosen);
        Assert.False(both.BringFlavourChosen);

        // Hidden rows never report a choice; no notes means no what-changed row; anonymous is a sentence.
        var greet = Make(new WelcomeBackPlan(true, false, false, null), name: "");
        Assert.False(F<Border>(greet, "RowRestore").IsVisible || F<Border>(greet, "RowFlavour").IsVisible);
        Assert.False(F<StackPanel>(greet, "RowWhatsNew").IsVisible);
        Assert.DoesNotContain(",", F<TextBlock>(greet, "TxtHeading").Text);
        F<CheckBox>(greet, "ChkRestore").IsChecked = F<CheckBox>(greet, "ChkFlavour").IsChecked = true;   // ticked but hidden
        greet.Commit();
        Assert.False(greet.RestoreChosen || greet.BringFlavourChosen);

        // Esc is "Let's go with nothing ticked".
        var esc = Make(new WelcomeBackPlan(true, true, true, "circe"));
        esc.Show();
        esc.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(esc.IsVisible);
        Assert.False(esc.RestoreChosen || esc.BringFlavourChosen);
        return Task.CompletedTask;
    });

    // ---- the logo ------------------------------------------------------------------------------

    [Fact]
    public void TheLogoRestsAtADriveOfAThirdAndRunsThreeTimesFasterOnHover()
    {
        Assert.Equal(0.35, AnimatedLogoImage.Drive(0), 6);
        Assert.Equal(1.0, AnimatedLogoImage.Drive(1), 6);
        Assert.Equal(Math.Tau / 12, AnimatedLogoImage.PhaseRate(0), 6);
        Assert.Equal(3.0, AnimatedLogoImage.PhaseRate(1) / AnimatedLogoImage.PhaseRate(0), 6);
    }

    [Fact]
    public Task TheLogoAnimatesOnTheBeatAndParksWhenHiddenOrMinimised() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var time = new SteppedTime();
        BeatLoop.Time = time;
        Window? w = null;
        try
        {
            var logo = new AnimatedLogoImage { Width = 160, Height = 160 };
            w = new Window { Content = logo, Width = 300, Height = 300 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(logo.IsAnimatedArtwork);
            Assert.False(logo.IsAnimating);           // the chrome gate is shut until the shell opens it
            logo.AmbientAllowed = true;
            Assert.True(logo.IsAnimating);

            var phase = typeof(AnimatedLogoImage).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var frame = typeof(AnimatedLogoImage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            time.Advance(TimeSpan.FromSeconds(0.1));
            BeatLoop.StepAll();
            Assert.Equal(AnimatedLogoImage.PhaseRate(0) * 0.1, (double)phase.GetValue(logo)!, 6);   // rest clock
            Assert.NotNull(logo.PendingFrame);                       // drawn off the UI thread
            Assert.True(logo.PendingFrame!.Wait(TimeSpan.FromSeconds(30)));
            Dispatcher.UIThread.RunJobs();                           // the finished frame lands on the UI thread
            Assert.Null(logo.PendingFrame);
            Assert.NotNull(frame.GetValue(logo));

            w.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.False(logo.IsAnimating);
            w.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(logo.IsAnimating);
            logo.IsVisible = false;
            Assert.False(logo.IsAnimating);
            logo.IsVisible = true;
            Assert.True(logo.IsAnimating);
            logo.AmbientAllowed = false;
            Assert.False(logo.IsAnimating);

            // A mod's own logo is drawn still, whatever the gate says (WPF SetArtwork :59).
            logo.AmbientAllowed = true;
            using var custom = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(8, 4), new Vector(96, 96));
            logo.SetArtwork(custom);
            Assert.False(logo.IsAnimatedArtwork);
            Assert.False(logo.IsAnimating);
        }
        finally
        {
            w?.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
        }
        return Task.CompletedTask;
    });
}
