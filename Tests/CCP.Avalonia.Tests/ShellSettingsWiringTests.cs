using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ShellSettingsWiringTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task HelpTourSpeaksOnTheVisibleDeskAndPanicClearsSpeech(bool complete) => Run(async (shell, settings) =>
    {
        settings.Current.EmiDeskEnabled = true;
        settings.Current.EmiDeskMuteAvatar = false;
        var desk = ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiDeskService.Instance;
        try
        {
            await desk.Summon("test");
            Assert.True(desk.IsOut);
            Click(shell.Named<Button>("BtnMainHelp")!);
            Click(shell.Named<Button>("BtnTutorialGettingStarted")!);
            Assert.Equal(".", desk.Window!.TourSpeechText);
            if (complete)
            {
                var startedLine = desk.Window.TourSpeechLine;
                while (TutorialHead.Service.IsActive) TutorialHead.Service.Next();
                Assert.NotEqual(startedLine, desk.Window.TourSpeechLine);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(EmiTourNarrator.Active);
                TutorialHead.StartOnShell(TutorialType.GettingStarted);
            }
            TutorialHead.OnPanic();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(desk.Window.TourSpeechText);
            Assert.Null(EmiTourNarrator.Active);
            TutorialHead.StartOnShell(TutorialType.GettingStarted);
            Assert.Null(desk.Window.TourSpeechText);
        }
        finally { desk.Window?.Close(); }
    });

    [Theory]
    [InlineData("tourStarted", true)]
    [InlineData("specific", true)]
    [InlineData("tourStep", false)]
    [InlineData("tourFinished", true)]
    [InlineData("tourSkipped", false)]
    public Task TourNarrationMapsEachMoment(string expected, bool complete) => Run(async (_, _) =>
    {
        var tutorial = new TutorialService();
        var moments = new List<string>();
        using var narrator = new EmiTourNarrator(tutorial, moments.Add, _ => expected == "specific");
        tutorial.Start(TutorialType.GettingStarted);
        if (expected == "specific") expected = "tour." + tutorial.CurrentStep!.Id;
        if (complete) while (tutorial.IsActive) tutorial.Next();
        else tutorial.Skip();
        Assert.Contains(expected, moments);
        await Task.CompletedTask;
    });

    [Fact]
    public Task DisposedNarratorDoesNotSpeakOnAnotherTour() => Run(async (_, _) =>
    {
        var tutorial = new TutorialService();
        var moments = new List<string>();
        var narrator = new EmiTourNarrator(tutorial, moments.Add, _ => false);
        tutorial.Start(TutorialType.GettingStarted);
        tutorial.Skip();
        int count = moments.Count;
        narrator.Dispose();
        tutorial.Start(TutorialType.GettingStarted);
        tutorial.Skip();
        Assert.Equal(count, moments.Count);
        await Task.CompletedTask;
    });

    [Fact]
    public Task MissingStepPoolFallsBackAndNarrationFailureDoesNotBlockTheTour() => Run(async (_, _) =>
    {
        var tutorial = new TutorialService();
        var moments = new List<string>();
        using var narrator = new EmiTourNarrator(tutorial, id =>
        {
            moments.Add(id);
            throw new InvalidOperationException("desk unavailable");
        }, _ => false);
        tutorial.Start(TutorialType.GettingStarted);
        Assert.True(tutorial.IsActive);
        Assert.Equal(new[] { "tourStarted", "tourStep" }, moments);
        tutorial.Skip();
        Assert.Equal("tourSkipped", moments.Last());
        await Task.CompletedTask;
    });

    [Fact]
    public Task TourBubbleUsesDeskPaletteAndStaysInsideTheWorkArea() => Run(async (_, _) =>
    {
        var desk = new ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiDeskWindow();
        try
        {
            desk.Show();
            var area = desk.Screens.ScreenFromWindow(desk)!.WorkingArea;
            desk.Position = new PixelPoint(area.Right - (int)(desk.Width * desk.RenderScaling / 2), area.Y + 200);
            desk.SayTourLine("A tour line", "^_^");
            var bubble = desk.BubbleHost.Children.OfType<Border>().Single();
            var text = Assert.IsType<TextBlock>(bubble.Child);
            Assert.Equal(global::Avalonia.Media.Color.Parse("#181832"), Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(bubble.Background).Color);
            Assert.Equal(global::Avalonia.Media.Color.Parse("#FF69B4"), Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(11, text.FontSize);
            Assert.Equal(330, bubble.MaxWidth);
            var left = desk.PointToScreen(new Point(Canvas.GetLeft(bubble), Canvas.GetTop(bubble)));
            var right = desk.PointToScreen(new Point(Canvas.GetLeft(bubble) + bubble.DesiredSize.Width, Canvas.GetTop(bubble)));
            Assert.True(left.X >= area.X, $"left {left.X}, work area {area}");
            Assert.True(right.X <= area.Right, $"right {right.X}, work area {area}");
        }
        finally { desk.Close(); }
        await Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, ".", 420)]
    [InlineData(1, "..", 420)]
    [InlineData(2, "...", 520)]
    [InlineData(3, "A tour line", 4050)]
    [InlineData(4, null, 0)]
    public Task TourSpeechUsesLockedCadenceAndCancellationClearsIt(int stage, string? expected, int interval) => Run(async (_, _) =>
    {
        var desk = new ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiDeskWindow();
        try
        {
            desk.Show();
            desk.SayTourLine("A tour line", "^_^");
            for (int i = 0; i < Math.Min(stage, 3); i++) desk.AdvanceTourSpeech();
            if (stage == 4) desk.CancelChain();
            Assert.Equal(expected, desk.TourSpeechText);
            if (stage != 4) Assert.Equal(interval, desk.TourSpeechInterval);
            else { desk.AdvanceTourSpeech(); Assert.Null(desk.TourSpeechText); }
        }
        finally { desk.Close(); }
        await Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task SaveDrawsTheTickAndRippleUnlessMotionIsOff(MotionLevel level) => Run(async (shell, service) =>
    {
        service.Current.MotionLevel = level;
        var tick = shell.Named<global::Avalonia.Controls.Shapes.Path>("SaveTick")!;
        var ripple = shell.Named<Border>("SaveRipple")!;
        Click(shell.Named<Button>("BtnSaveAll")!);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        if (level == MotionLevel.Off)
        {
            Assert.Equal(0, tick.Opacity);
            Assert.Equal(0, ripple.Opacity);
        }
        else
        {
            Assert.True(tick.Opacity > 0.5, $"tick opacity {tick.Opacity}");
            // The ripple fades 0.75 -> 0 in 520ms of wall clock; a slow runner reads it late (0.097 seen
            // in rgate 39872884951), so only "it started" is stable. Off stays exactly 0.
            Assert.True(ripple.Opacity > 0, $"ripple opacity {ripple.Opacity}");
        }
        (await Dialog<MessageDialog>(shell)).Close();
    });

    [Fact]
    public Task HelpRowsStartTheirTourAndPanicRestoresTheBrowser() => Run(async (shell, _) =>
    {
        var browser = shell.Named<SettingsTabView>("SettingsTab")!.BrowserContainer;
        foreach (var name in new[] { "GettingStarted", "Settings", "Presets", "Progression", "Achievements", "Companion", "Patreon", "Avatar", "FullTour" })
        {
            Click(shell.Named<Button>("BtnMainHelp")!);
            Assert.True(shell.Named<Control>("MainTutorialOverlay")!.IsVisible);
            Assert.False(browser.IsVisible);
            Click(shell.Named<Button>(name == "FullTour" ? "BtnStartTutorial" : "BtnTutorial" + name)!);
            Assert.True(CoreTutorial.IsActive);
            Assert.NotNull(EmiTourNarrator.Active);
            Assert.Equal(name, CoreTutorial.CurrentTourName);
            Assert.False(shell.Named<Control>("MainTutorialOverlay")!.IsVisible);
            Assert.False(browser.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.Null(EmiTourNarrator.Active);
            Assert.True(browser.IsVisible);
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task SavePersistsBeforeOfferingANewPresetAndKeepsItsSnapshot() => Run(async (shell, service) =>
    {
        service.Current.CurrentPresetName = "";
        service.Current.FlashFrequency = 42;
        Click(shell.Named<Button>("BtnSaveAll")!);
        var offer = await Dialog<MessageDialog>(shell);
        Assert.Equal(Loc.Get("title_settings_saved"), offer.Title);
        Assert.True(service.HasPendingSave); // saved before the offer, not after it
        Click(offer.FindControl<Button>("BtnOk")!);
        var input = await Dialog<InputDialog>(shell);
        input.FindControl<TextBox>("TxtInput")!.Text = "Saved from shell";
        Click(input.FindControl<Button>("BtnOK")!);
        var notice = await Dialog<MessageDialog>(shell);
        Assert.Equal(Loc.Get("title_preset_saved"), notice.Title);
        var preset = Assert.Single(service.Current.UserPresets);
        Assert.Equal("Saved from shell", preset.Name);
        Assert.Equal(42, preset.FlashFrequency);
        notice.Close();
    });

    [Fact]
    public Task SaveOverwriteCancelAndSaveAsKeepPresetIdentity() => Run(async (shell, service) =>
    {
        var original = Preset.FromSettings(service.Current, "Mine", "original description");
        service.Current.UserPresets.Add(original);
        service.Current.CurrentPresetName = original.Name;
        service.Current.FlashFrequency = 17;
        Click(shell.Named<Button>("BtnSaveAll")!);
        var overwriteOffer = await Dialog<MessageDialog>(shell);
        if (Environment.GetEnvironmentVariable("CCP_SETTINGS_EVIDENCE_DIR") is { Length: > 0 } evidence)
        {
            Directory.CreateDirectory(evidence);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = overwriteOffer.CaptureRenderedFrame();
            frame?.Save(Path.Combine(evidence, "SettingsPresetOffer.png"));
        }
        Click(overwriteOffer.FindControl<Button>("BtnOk")!);
        var notice = await Dialog<MessageDialog>(shell);
        var updated = Assert.Single(service.Current.UserPresets);
        Assert.Equal((original.Id, original.CreatedAt, original.Description, 17),
            (updated.Id, updated.CreatedAt, updated.Description, updated.FlashFrequency));
        notice.Close();

        service.Current.FlashFrequency = 19;
        Click(shell.Named<Button>("BtnSaveAll")!);
        Click((await Dialog<MessageDialog>(shell)).FindControl<Button>("BtnCancel")!);
        Assert.Same(updated, Assert.Single(service.Current.UserPresets));
        Assert.Equal(17, updated.FlashFrequency);

        Click(shell.Named<Button>("BtnSaveAll")!);
        Click((await Dialog<MessageDialog>(shell)).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BtnNo"));
        var input = await Dialog<InputDialog>(shell);
        input.FindControl<TextBox>("TxtInput")!.Text = "Another";
        Click(input.FindControl<Button>("BtnOK")!);
        notice = await Dialog<MessageDialog>(shell);
        Assert.Equal(2, service.Current.UserPresets.Count);
        Assert.Equal(19, service.Current.UserPresets.Single(p => p.Name == "Another").FlashFrequency);
        Assert.Equal(17, service.Current.UserPresets.Single(p => p.Name == "Mine").FlashFrequency);
        notice.Close();

        service.Current.CurrentPresetName = "";
        Click(shell.Named<Button>("BtnSaveAll")!);
        Click((await Dialog<MessageDialog>(shell)).FindControl<Button>("BtnOk")!);
        input = await Dialog<InputDialog>(shell);
        input.FindControl<TextBox>("TxtInput")!.Text = "mine";
        Click(input.FindControl<Button>("BtnOK")!);
        notice = await Dialog<MessageDialog>(shell);
        Assert.Equal(Loc.Get("title_name_taken"), notice.Title);
        Assert.Equal(2, service.Current.UserPresets.Count);
        notice.Close();
    });

    [Fact]
    public Task AwarenessGuideOpensPuppyOnlyAfterNaturalCompletion() => Run(async (shell, _) =>
    {
        void Start() { Click(shell.Named<Button>("BtnMainHelp")!); Click(shell.Named<Button>("BtnTutorialAwareness")!); }
        Start();
        Assert.Equal("Awareness", CoreTutorial.CurrentTourName);
        CoreTutorial.Skip();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<AwarenessPresetDetailDialog>());
        Start();
        Assert.True(CoreTutorial.IsActive);
        while (CoreTutorial.IsActive) CoreTutorial.Next();
        var puppy = await Dialog<AwarenessPresetDetailDialog>(shell);
        Assert.True(puppy.IsVisible);
        puppy.Close();
    });

    [Fact]
    public Task ModdingAndReportRowsOpenTheirOwnedWindows() => Run(async (shell, _) =>
    {
        Click(shell.Named<Button>("BtnMainHelp")!);
        Click(shell.Named<Button>("BtnTutorialModding")!);
        var creator = await Dialog<ModCreatorWindow>(shell);
        Assert.Equal("Modding", CoreTutorial.CurrentTourName);
        Assert.True(CoreTutorial.IsActive);
        PanicSurfaces.All.Single(s => s.Id == "tour").Stop(shell);
        Assert.False(CoreTutorial.IsActive);
        creator.Close();
        Click(shell.Named<Button>("BtnMainHelp")!);
        Click(shell.Named<Button>("BtnTutorialReportBug")!);
        var report = await Dialog<BugReportWindow>(shell);
        Assert.False(shell.Named<Control>("MainTutorialOverlay")!.IsVisible);
        report.Close();
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task FolderChangeOffersPacksFromBothRootsAndRespectsTheAnswer(bool move) => Run(async (shell, service) =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-settings-move-").FullName;
        var old = Path.Combine(root, "old");
        var defaults = Path.Combine(root, "default");
        var selected = Path.Combine(root, "new");
        WritePack(old, "one"); WritePack(defaults, "two");
        var oldProvider = CorePaths.EffectiveAssetsProvider;
        var oldStore = ContentPackStore.Current;
        service.Current.CustomAssetsPath = old;
        CorePaths.EffectiveAssetsProvider = () => service.Current.CustomAssetsPath;
        ContentPackStore.Current = new ContentPackStore(() => CorePaths.EffectiveAssets, () => service.Current, () => { });
        try
        {
            var apply = MainShellWindow.ApplyAssetsFolder(shell, selected, old, defaults);
            var offer = await Dialog<MessageDialog>(shell);
            Assert.Equal(Loc.Get("title_move_downloaded_packs"), offer.Title);
            var question = offer.FindControl<TextBlock>("TxtMessage")!.Text!;
            Assert.Contains("Pack one", question); Assert.Contains("Pack two", question);
            Click(offer.FindControl<Button>(move ? "BtnOk" : "BtnCancel")!);
            var done = await Dialog<MessageDialog>(shell);
            Assert.Equal(Loc.Get("title_assets_folder_set"), done.Title);
            Assert.Equal(selected, service.Current.CustomAssetsPath);
            Assert.True(Directory.Exists(Path.Combine(selected, "images")));
            Assert.True(Directory.Exists(Path.Combine(selected, "videos")));
            Assert.Equal(move, Directory.Exists(Path.Combine(selected, ".packs", "one")));
            Assert.Equal(move, Directory.Exists(Path.Combine(selected, ".packs", "two")));
            Assert.Equal(!move, Directory.Exists(Path.Combine(old, ".packs", "one")));
            Assert.Equal(!move, Directory.Exists(Path.Combine(defaults, ".packs", "two")));
            Assert.Equal(move ? 2 : 0, ContentPackStore.Current.GetActivePackIds().Count);
            done.Close();
            Assert.Equal(selected, await apply);
        }
        finally
        {
            CorePaths.EffectiveAssetsProvider = oldProvider;
            ContentPackStore.Current = oldStore;
            Directory.Delete(root, true);
        }
    });

    private static void WritePack(string root, string id)
    {
        var path = Path.Combine(root, ".packs", id);
        Directory.CreateDirectory(Path.Combine(path, "content"));
        File.WriteAllBytes(Path.Combine(path, "content", "one.enc"), PackEncryptionService.Encrypt(new byte[] { 1, 2, 3 }));
        var manifest = new InstalledPackManifest { PackId = id, PackGuid = id, PackName = "Pack " + id,
            Files = new() { new PackFileEntry { OriginalName = "one.png", FileType = "image", Extension = ".png", ObfuscatedName = "one.enc" } } };
        PackEncryptionService.SaveEncryptedManifest(Newtonsoft.Json.JsonConvert.SerializeObject(manifest), Path.Combine(path, ".manifest.enc"));
    }

    private sealed class Store : TutorialService.ITourCompletionStore
    {
        private readonly HashSet<string> _walked = new();
        public bool Has(string name) => _walked.Contains(name);
        public void Latch(string name) => _walked.Add(name);
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<T> Dialog<T>(Window shell) where T : Window
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            if (shell.OwnedWindows.OfType<T>().FirstOrDefault(w => w.IsVisible) is { } dialog) return dialog;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        throw new InvalidOperationException("Expected owned " + typeof(T).Name);
    }

    private static Task Run(Func<MainShellWindow, SettingsService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = CoreSettings.ServiceProvider;
        var oldShell = TutorialHeadHooks.ShellProvider;
        var oldStore = TutorialService.CompletionStore;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.PerformanceMode = true;
        service.Current.UserPresets.Clear();
        service.Current.CurrentPresetName = "";
        TutorialService.CompletionStore = new Store();
        ConditioningControlPanel.Services.EmiDesk.EmiLineEngine.Instance.ResetForTests();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var shell = new MainShellWindow();
        TutorialHeadHooks.ShellProvider = () => shell;
        try { shell.Show(); Dispatcher.UIThread.RunJobs(); await body(shell, service); }
        finally
        {
            TutorialHead.ResetForTests();
            ConditioningControlPanel.Services.EmiDesk.EmiLineEngine.Instance.ResetForTests();
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            service.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            TutorialHeadHooks.ShellProvider = oldShell;
            TutorialService.CompletionStore = oldStore;
        }
    });
}
