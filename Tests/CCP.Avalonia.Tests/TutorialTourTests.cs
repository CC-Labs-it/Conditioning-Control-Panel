using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The tour engine on this head (ledger G1 / H4 / P8 / T15): WPF Services/TutorialService.cs behind
/// the CoreTutorial seam, and the coach mark drawn INSIDE the host window.
/// Ports WPF ShortWalkTourTests' engine half (the seven cards, the completion ledger).
/// Runs alone: it opens a shell and swaps process-wide seams (the tour service is one per process).
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TutorialTourTests : IDisposable
{
    private sealed class FakeStore : TutorialService.ITourCompletionStore
    {
        public readonly List<string> Latched = new();
        public bool Has(string name) => Latched.Contains(name, StringComparer.OrdinalIgnoreCase);
        public void Latch(string name) { if (!Has(name)) Latched.Add(name); }
    }

    private readonly FakeStore _store = new();
    private readonly Func<MainShellWindow?> _shellBefore = TutorialHeadHooks.ShellProvider;

    public TutorialTourTests()
    {
        // Never the real emi-desk.json: a tour walked to its end in a test would latch there.
        TutorialService.CompletionStore = _store;
    }

    public void Dispose()
    {
        TutorialService.CompletionStore = null!;
        TutorialHeadHooks.ShellProvider = _shellBefore;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    // ---- the service (WPF ShortWalkTourTests) --------------------------------------------------

    [Fact]
    public void ShortWalkHasTheSevenContractStepsInOrder()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);
        Assert.Equal(
            new[] { "sw-assets", "sw-flash", "sw-panic", "sw-dock", "sw-xp", "sw-settings", "sw-done" },
            svc.CurrentSteps.Select(s => s.Id).ToArray());
        Assert.Equal(7, svc.TotalSteps);
    }

    [Fact]
    public void WalkingOffTheEndLatchesTheTourAndSkippingNeverDoes()
    {
        var svc = new TutorialService();
        var endings = new List<bool>();
        svc.TutorialFinished += (_, e) => endings.Add(e.Completed);

        svc.Start(TutorialType.ShortWalk);
        svc.Skip();
        Assert.False(svc.IsActive);
        Assert.False(svc.HasCompleted(TutorialType.ShortWalk));

        svc.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 7; i++) svc.Next();
        Assert.False(svc.IsActive);
        Assert.True(svc.HasCompleted(TutorialType.ShortWalk));
        Assert.Equal(new[] { false, true }, endings);

        svc.Skip();                                   // a second ending is not a second event
        Assert.Equal(2, endings.Count);
    }

    [Fact]
    public void EveryTourHasCardsWithTextAndUniqueIds()
    {
        foreach (var group in TutorialHead.AllSteps().GroupBy(x => x.Tour))
        {
            var steps = group.Select(x => x.Step).ToList();
            Assert.True(steps.Count > 0, group.Key + " has no cards");
            Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
            foreach (var s in steps)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Title), $"{group.Key}/{s.Id} title");
                Assert.False(string.IsNullOrWhiteSpace(s.Description), $"{group.Key}/{s.Id} body");
                Assert.DoesNotContain('—', s.Title + s.Description);
                Assert.DoesNotContain('–', s.Title + s.Description);
            }
        }
    }

    /// <summary>A step whose target does not exist on this head is LISTED, never silently lost:
    /// every target name is a Name in some axaml here, or sits in TargetsNotOnThisHead (and a name
    /// in that list really is absent, so the list cannot rot).</summary>
    [Fact]
    public void EveryTourTargetIsANamedControlOrIsListed()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "CCP.Avalonia"), "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "Name=\"([A-Za-z0-9_]+)\""))
                names.Add(m.Groups[1].Value);
        }

        // Code-built inspector fields are covered by NewEnhancementTutorialTests.
        names.UnionWith(new[] { "TutorialTriggerTimeField", "TutorialActionIntensityField" });
        var targets = TutorialHead.AllSteps().Select(x => x.Step.TargetElementName).Where(n => n != null).Distinct().ToList();
        Assert.True(targets.Count > 50, "the tours lost their targets");

        var unknown = targets.Where(n => !names.Contains(n!) && !TutorialHead.TargetsNotOnThisHead.Contains(n)).ToList();
        Assert.True(unknown.Count == 0, "Tour targets with no control and no listing: " + string.Join(", ", unknown));

        var stale = TutorialHead.TargetsNotOnThisHead.Where(names.Contains).ToList();
        Assert.True(stale.Count == 0, "Listed as missing but present: " + string.Join(", ", stale));
    }

    [Fact]
    public void TheSeamProjectsTheHeadStepAndAnUnknownNameStartsNothing()
    {
        var step = new TutorialStep
        {
            Id = "x", Title = "T", Description = "D", Icon = "!", TargetElementName = "BtnPanicKey",
            TextPosition = TutorialStepPosition.Left, AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "FileSaved", AllowManualSkip = true, BlockBackgroundClicks = false,
        };
        var core = TutorialHead.Project(step);
        Assert.Same(core, TutorialHead.Project(step));
        Assert.Equal("BtnPanicKey", core.TargetElementName);
        Assert.Equal(CoreTutorial.StepPosition.Left, core.TextPosition);
        Assert.Equal(CoreTutorial.AdvanceTrigger.OnEvent, core.Advance);
        Assert.Equal("FileSaved", core.AdvanceEventName);
        Assert.True(core.AllowManualSkip);
        Assert.False(core.BlockBackgroundClicks);

        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        TutorialHeadHooks.ShellProvider = () => null;
        CoreTutorial.Start("NoSuchTour");
        Assert.False(CoreTutorial.IsActive);
        CoreTutorial.Start("ShortWalk");              // a shell tour with no shell draws nothing
        Assert.False(CoreTutorial.IsActive);
    }

    // ---- the shell (adapted: this head's overlay is still its own window; the in-window stage,
    // the ? panel's StartGuide and TextMatches arrive with TutorialOverlay.Live.cs, a follow-up) ----

    private static TutorialOverlay? ShellOverlay() =>
        (TutorialOverlay?)typeof(TutorialHead).GetField("_shellOverlay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public void StepIconsMapThroughIconMap()
    {
        Assert.True(IconMap.TryGet("\U0001F4BE", out var save));                  // a tour's save icon
        Assert.Equal(save.Kind, TutorialOverlay.IconFor("\U0001F4BE").Kind);
        Assert.Equal(FluentIcons.Common.Symbol.Sparkle, TutorialOverlay.IconFor(">").Kind);       // ASCII marks are not drawn as text
    }

    [Fact]
    public Task ATourStartsStepsClosesAndPanicEndsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        bool perfBefore = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;        // Forever FX loops make RunJobs spin
        var panicBefore = PanicSurfaces.All;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;

            // Start: the seam every page calls puts the overlay on the shell.
            CoreTutorial.Start("GettingStarted");
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);
            var overlay = ShellOverlay();
            Assert.NotNull(overlay);
            Assert.True(overlay!.IsVisible);
            var title = Named<TextBlock>(overlay, "TxtTitle");
            Assert.Equal(CoreTutorial.CurrentStep!.Title, title.Text);

            // The step's Icon string is data: a Fluent IconGlyph is drawn, never the emoji text.
            var icon = CoreTutorial.CurrentStep!.Icon;
            Assert.False(string.IsNullOrEmpty(icon));
            var glyph = Named<IconGlyph>(overlay, "TxtIcon");
            Assert.Null(glyph.Emoji);
            Assert.False(glyph.IsFallback);
            Assert.Equal(TutorialOverlay.IconFor(icon).Kind, glyph.Kind);
            Assert.DoesNotContain(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == icon);

            // Step: Next walks, Previous walks back.
            Named<Button>(overlay, "BtnNext").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, CoreTutorial.CurrentStepIndex);
            Assert.Equal(CoreTutorial.CurrentStep!.Title, title.Text);
            Named<Button>(overlay, "BtnPrevious").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);

            // One tour at a time (WPF: StartTutorial returns while an overlay is up).
            CoreTutorial.Start("Settings");
            Assert.Same(overlay, ShellOverlay());
            Assert.Equal("GettingStarted", CoreTutorial.CurrentTourName);   // a refused start renames nothing

            // Panic: the registered "tour" surface ends it by the abandon route and the overlay closes.
            Assert.Contains(PanicSurfaces.All, s => s.Id == "tour");
            PanicSurfaces.All.First(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.Null(ShellOverlay());
            Assert.False(overlay.IsVisible);
            Assert.False(TutorialHead.Service.HasCompleted(TutorialType.GettingStarted));

            // Close: a fresh tour starts after that, and Escape on the card abandons it too.
            CoreTutorial.Start("ShortWalk");
            Dispatcher.UIThread.RunJobs();
            var second = ShellOverlay();
            Assert.NotNull(second);
            Assert.NotSame(overlay, second);
            second!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.Null(ShellOverlay());
            Assert.False(TutorialHead.Service.HasCompleted(TutorialType.ShortWalk));
        }
        finally
        {
            TutorialHead.ResetForTests();
            PanicSurfaces.All = panicBefore;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.PerformanceMode = perfBefore;
            CoreSettings.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
