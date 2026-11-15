using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AvatarTubeFinishTests
{
    private static Task Run(Action<MainShellWindow, AvatarTubeWindow, Action<ModPackage>> body,
        int savedSet = 1, int? remembered = null) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        var settingsProvider = CoreSettings.ServiceProvider;
        var packageProvider = CoreMods.ActiveModPackageProvider;
        var idProvider = CoreMods.ActiveModIdProvider;
        var switchCompanion = CoreModsHooks.SwitchCompanion;
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.AvatarEnabled = true;
        s.AvatarTubeDetached = false;
        s.SelectedAvatarSet = savedSet;
        s.AutonomyModeEnabled = s.AutonomyResumeOnStartup = s.AwarenessModeEnabled = false;
        s.PanicKeyEnabled = false;
        if (remembered.HasValue) s.ModAvatarSet["tube-test"] = remembered.Value;
        var mod = new ModPackage(new ModManifest { Id = "tube-test" }, null, false);
        CoreMods.ActiveModPackageProvider = () => mod;
        CoreMods.ActiveModIdProvider = () => mod.Id;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(shell.Tube);
            body(shell, shell.Tube!, next =>
            {
                mod = next;
                CoreMods.RaiseModChanged(null, mod);
                Dispatcher.UIThread.RunJobs();
            });
        }
        finally
        {
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = settingsProvider;
            CoreMods.ActiveModPackageProvider = packageProvider;
            CoreMods.ActiveModIdProvider = idProvider;
            CoreModsHooks.SwitchCompanion = switchCompanion;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LookPickerUsesWpfOrderAndSwitchesThroughOnePersistedPath() => Run((shell, tube, _) =>
    {
        var switches = new List<CompanionId>();
        CoreModsHooks.SwitchCompanion = id => switches.Add(id);
        Action finish = () => { };
        tube.AvatarSwitchScheduler = (delay, fire) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), delay);
            finish = fire;
            return () => { };
        };
        var card = new CompanionPickerCard();
        var host = new Window { Content = card, Width = 800, Height = 600 };
        try
        {
            host.Show(shell);
            card.Refresh();
            var combo = card.FindControl<ComboBox>("CmbAvatar")!;
            Assert.True(combo.IsEnabled);
            Assert.Equal(new[] { 1, 2, 3, 4, 7, 5, 6 },
                combo.Items.Cast<ComboBoxItem>().Select(i => (int)i.Tag!));
            combo.SelectedIndex = 4;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(7, tube.CurrentAvatarSet);
            Assert.Equal(7, CoreSettings.Current.SelectedAvatarSet);
            Assert.Equal(7, CoreSettings.Current.ModAvatarSet["tube-test"]);
            Assert.Empty(switches);
            finish();
            Assert.Equal(new[] { CompanionId.BimboCow }, switches);
            Assert.False(tube.SelectAvatarSet(7));
            Assert.False(tube.SelectAvatarSet(999));
        }
        finally { host.Close(); }
    });

    [Fact]
    public Task PanicLeavesPendingLookPickerFadeAndCompanionCouplingIntact() => Run((_, tube, _) =>
    {
        Action? finish = null;
        int canceled = 0;
        var switches = new List<CompanionId>();
        CoreModsHooks.SwitchCompanion = id => switches.Add(id);
        tube.AvatarSwitchScheduler = (delay, fire) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), delay);
            finish = fire;
            return () => canceled++;
        };
        var title = tube.FindControl<TextBlock>("TxtAvatarTitle")!;
        var before = title.Text;
        Assert.True(tube.SelectAvatarSet(4));
        Assert.NotNull(finish);
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var oldArt = image.Source;
        Assert.NotNull(oldArt);
        tube.PanicSilence();
        Assert.Equal(0, canceled); // fail-proof: unconditional panic cancellation drops the picker paint
        Assert.Empty(switches);
        Assert.Equal(before, title.Text);
        finish!(); // WPF's existing fade still completes after panic
        Assert.Equal(new[] { CompanionId.CultBunny }, switches);
        Assert.Equal(4, tube.CurrentAvatarSet);
        Assert.Equal(4, CoreSettings.Current.SelectedAvatarSet);
        Assert.Equal(4, CoreSettings.Current.ModAvatarSet["tube-test"]);
        Assert.Equal(AvatarTubeWindow.AvatarSetTitle(4).ToUpperInvariant(), title.Text);
        Assert.NotSame(oldArt, image.Source);
        Assert.Equal(1, tube.FindControl<Border>("AvatarBorder")!.GetBaseValue(Visual.OpacityProperty).Value);
    });

    [Fact]
    public Task TitleArrowAcceptsOnlyLeftClicksAndUsesTheSameSelector() => Run((_, tube, _) =>
    {
        tube.AvatarSwitchScheduler = (_, _) => () => { };
        var arrow = tube.FindControl<Border>("BtnNextAvatar")!;
        Assert.True(arrow.IsVisible);
        var at = arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), tube)!.Value;
        tube.MouseDown(at, MouseButton.Right);
        tube.MouseUp(at, MouseButton.Right);
        Assert.Equal(1, tube.CurrentAvatarSet);
        tube.MouseDown(at, MouseButton.Left);
        tube.MouseUp(at, MouseButton.Left);
        Assert.Equal(2, tube.CurrentAvatarSet);
        Assert.Equal(2, CoreSettings.Current.ModAvatarSet["tube-test"]);
    });

    [Fact]
    public Task OpeningRestoresTheModsRememberedLook() => Run((_, tube, _) =>
    {
        Assert.Equal(4, tube.CurrentAvatarSet);
        Assert.Equal(2, CoreSettings.Current.SelectedAvatarSet); // opening is not a new pick
    }, savedSet: 2, remembered: 4);

    [Fact]
    public Task ModSwitchRestoresSupportedLookAndClosedTubeUnsubscribes() => Run((_, tube, change) =>
    {
        var s = CoreSettings.Current;
        s.ModAvatarSet["other"] = 8;
        var other = new ModPackage(new ModManifest
        {
            Id = "other", SupportedAvatarSets = new() { 2, 8, 9 },
            CustomAvatarSets = new()
            {
                new() { SetNumber = 9, UnlockLevel = 90, Label = "Later" },
                new() { SetNumber = 8, UnlockLevel = 10, Label = "Earlier" },
            },
        }, null, false);
        change(other);
        Assert.Equal(8, tube.CurrentAvatarSet);
        Assert.Equal("Earlier", AvatarTubeWindow.AvatarSetTitle(8));
        Assert.Equal(new[] { 2, 8, 9 }, tube.PickableAvatarSets());
        Assert.True(tube.FindControl<Border>("BtnPrevAvatar")!.IsVisible);
        Assert.True(tube.FindControl<Border>("BtnNextAvatar")!.IsVisible);
        Assert.Equal(8, s.SelectedAvatarSet);
        Assert.Equal(8, s.ModAvatarSet["other"]);
        tube.Close();
        s.ModAvatarSet["other"] = 9;
        change(other);
        Assert.Equal(8, tube.CurrentAvatarSet);
    });

    [Fact]
    public Task RapidPicksSettleOnlyTheLastCompanionAndCloseCancelsTheFade() => Run((_, tube, _) =>
    {
        var callbacks = new List<Action>();
        var switched = new List<CompanionId>();
        tube.AvatarSwitchScheduler = (_, fire) => { callbacks.Add(fire); return () => { }; };
        CoreModsHooks.SwitchCompanion = id => switched.Add(id);
        tube.SelectAvatarSet(3);
        tube.SelectAvatarSet(4);
        callbacks[0](); // even a stale callback already posted to the dispatcher cannot repaint
        Assert.Empty(switched);
        callbacks[1]();
        Assert.Equal(new[] { CompanionId.CultBunny }, switched);
        Assert.Equal(1, tube.FindControl<Border>("AvatarBorder")!.GetBaseValue(Visual.OpacityProperty).Value);
        tube.SelectAvatarSet(5);
        tube.Close();
        callbacks[2]();
        Assert.Single(switched);
    });

    [Fact]
    public Task RosterFadesBeforePaintingAndRejectsSupersededAndClosedCallbacks() => Run((_, tube, _) =>
    {
        var callbacks = new List<Action>();
        int canceled = 0;
        tube.AvatarSwitchScheduler = (delay, fire) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), delay);
            callbacks.Add(fire);
            return () => canceled++;
        };
        var border = tube.FindControl<Border>("AvatarBorder")!;
        var title = tube.FindControl<TextBlock>("TxtAvatarTitle")!;
        var before = title.Text;
        tube.SwitchToCompanionAvatar(CompanionId.CultBunny);
        Assert.Single(callbacks); // fail-proof: an immediate paint never schedules the fade
        Assert.Equal(before, title.Text);
        Assert.Equal(0, border.GetBaseValue(Visual.OpacityProperty).Value);
        Assert.Equal(4, CoreSettings.Current.SelectedAvatarSet);
        Assert.Equal(4, CoreSettings.Current.ModAvatarSet["tube-test"]);
        var transition = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(border.Transitions!));
        Assert.Equal(Visual.OpacityProperty, transition.Property);
        Assert.Equal(TimeSpan.FromMilliseconds(200), transition.Duration);
        tube.SwitchToCompanionAvatar(CompanionId.BimboCow);
        callbacks[0](); // a callback posted before cancellation must not paint
        Assert.Equal(before, title.Text);
        Assert.Equal(1, canceled);
        callbacks[1]();
        Assert.Equal(AvatarTubeWindow.AvatarSetTitle(7).ToUpperInvariant(), title.Text);
        Assert.NotEqual(before, title.Text);
        Assert.Equal(7, tube.CurrentAvatarSet);
        Assert.Equal(1, border.GetBaseValue(Visual.OpacityProperty).Value);
        var settled = title.Text;
        tube.SwitchToCompanionAvatar(CompanionId.BrainParasite);
        tube.PanicSilence();
        Assert.Equal(AvatarTubeWindow.AvatarSetTitle(5).ToUpperInvariant(), title.Text);
        Assert.Null(border.Transitions);
        Assert.NotEqual(settled, title.Text); // panic settles the saved selection without waiting
        Assert.Equal(1, border.GetBaseValue(Visual.OpacityProperty).Value);
        settled = title.Text;
        callbacks[2]();
        Assert.Equal(settled, title.Text);
        tube.SwitchToCompanionAvatar(CompanionId.BambiTrainer);
        tube.Close();
        callbacks[3]();
        Assert.Equal(settled, title.Text);
        tube.SwitchToCompanionAvatar(CompanionId.OGBambiSprite);
        Assert.Equal(4, callbacks.Count);
    });

    [Fact]
    public Task RosterPreservesSingleEmoteLookAndSavedSelection() => Run((_, tube, change) =>
    {
        change(new ModPackage(new ModManifest { Id = "builtin-bambisleep" }, null, true));
        var title = tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text;
        tube.AvatarSwitchScheduler = (_, _) => throw new InvalidOperationException("pinned look must not fade");
        tube.SwitchToCompanionAvatar(CompanionId.BimboCow);
        Assert.Equal(1, tube.CurrentAvatarSet);
        Assert.Equal(4, CoreSettings.Current.SelectedAvatarSet);
        Assert.False(CoreSettings.Current.ModAvatarSet.ContainsKey("builtin-bambisleep"));
        Assert.Equal(title, tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
    }, savedSet: 4);

    [Fact]
    public Task SingleEmoteModPinsItsLookWithoutOverwritingTheGlobalPick() => Run((_, tube, change) =>
    {
        change(new ModPackage(new ModManifest { Id = "builtin-bambisleep" }, null, true));
        Assert.Equal(new[] { 1 }, tube.PickableAvatarSets());
        Assert.Equal(1, tube.CurrentAvatarSet);
        Assert.Equal(4, CoreSettings.Current.SelectedAvatarSet);
        Assert.False(tube.FindControl<Border>("BtnPrevAvatar")!.IsVisible);
        Assert.False(tube.FindControl<Border>("BtnNextAvatar")!.IsVisible);
    }, savedSet: 4);

    [Fact]
    public Task ModLocalEmotePinsItsSetUnlessOtherLooksAreDeclared() => Run((_, tube, change) =>
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-tube-emote-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "resources", "emotes", "set8");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{}");
        try
        {
            var manifest = new ModManifest { Id = "tube-local" };
            var mod = new ModPackage(manifest, root, false);
            change(mod);
            Assert.Equal(new[] { 8 }, tube.PickableAvatarSets());
            Assert.Equal(8, tube.CurrentAvatarSet);
            Assert.Equal(4, CoreSettings.Current.SelectedAvatarSet);
            manifest.SupportedAvatarSets = new() { 2, 8 };
            manifest.CustomAvatarSets = new() { new() { SetNumber = 8, Label = "Custom" } };
            change(mod);
            Assert.Equal(new[] { 2, 8 }, tube.PickableAvatarSets());
            Assert.True(tube.FindControl<Border>("BtnPrevAvatar")!.IsVisible);
        }
        finally { Directory.Delete(root, recursive: true); }
    }, savedSet: 4);

    [Fact]
    public Task DiskPortraitSkinsReplacePosesAndEmotesTakePrecedence() => Run((_, tube, change) =>
    {
        var (root, audio, mod) = PortraitFixture("portrait-test", """
            {"idleEmotion":"neutral","skins":[{"id":"a","dir":"a","title":"First"},{"id":"b","dir":"b","title":"Second"}],"emotions":{"neutral":{"portraits":[{"file":"idle.png"}]}}}
            """, new string[0]);
        try
        {
            change(mod);
            Assert.False(tube.IsPortraitMode); // manifest without images keeps the ordinary avatar
            Assert.Equal(7, tube.PickableAvatarSets().Length);
            foreach (var skin in new[] { "a", "b" }) CopyPose(audio, skin, "idle.png");
            change(mod);
            Assert.True(tube.IsPortraitMode); Assert.False(tube.IsPoseRotating);
            Assert.True(tube.RefreshRainPlayback().IsCompletedSuccessfully); Assert.False(tube.IsPoseRotating);
            Assert.Equal(new[] { 1, 2 }, tube.PickableAvatarSets());
            Assert.Equal("First", tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
            SelectNow(tube, 2);
            Assert.Equal("Second", tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
            Assert.False(tube.IsPoseRotating);
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, "resources", "emotes", "set1")).FullName, "emotes.json"), "{}");
            change(mod);
            Assert.False(tube.IsPortraitMode); Assert.Equal(new[] { 1 }, tube.PickableAvatarSets());
            change(new ModPackage(new ModManifest { Id = "other" }, null, false)); Assert.False(tube.IsPortraitMode);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task PortraitAllowsSelectedSkinWhenFirstSkinHasNoIdle() => Run((_, tube, change) =>
    {
        var (root, audio, mod) = PortraitFixture("missing-first", """
            {"idleEmotion":"neutral","skins":[{"id":"a","dir":"a"},{"id":"b","dir":"b","title":"Second"}],"emotions":{"neutral":{"portraits":[{"file":"idle.png"},{"file":"two.png"},{"file":"three.png"},{"file":"four.png"}]}}}
            """, new[] { "b" }, "idle.png", "two.png", "three.png", "four.png");
        try
        {
            change(mod);
            Assert.Equal(new[] { 1, 2 }, tube.PickableAvatarSets()); Assert.True(tube.IsPortraitMode);
            SelectNow(tube, 2);
            Assert.True(tube.IsPortraitMode); Assert.Equal("Second", tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
            Assert.Equal(Path.Combine(audio, "b", "idle.png"), tube.PortraitPath);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task PortraitSpeechUsesLineAndMoodCrossfadesAndReturnsToIdle() => Run((_, tube, change) =>
    {
        var (root, audio, mod) = PortraitFixture("portrait-speech", """
            {"idleEmotion":"neutral","director":{"crossfadeFrames":4},"skins":[{"id":"a","dir":"a"},{"id":"b","dir":"b"}],"emotions":{"neutral":{"portraits":[{"file":"idle.png"}]},"happy":{"portraits":[{"file":"happy1.png"},{"file":"happy2.png"}]},"tender":{"portraits":[{"file":"tender.png"}]},"other":{"portraits":[{"file":"happy3.png"}]}},"lines":{"mapped":{"emotion":"happy"}}}
            """, new[] { "a", "b" }, "idle.png", "happy1.png", "happy2.png", "happy3.png", "tender.png");
        try
        {
            using (var wave = new NAudio.Wave.WaveFileWriter(Path.Combine(audio, "mapped.wav"), new NAudio.Wave.WaveFormat(8000, 16, 1)))
                wave.Write(new byte[64000], 0, 64000); // four seconds
            change(mod);
            List<(TimeSpan Delay, Action Fire)> steps = new(), fades = new();
            tube.PortraitStepScheduler = (delay, fire) => { steps.Add((delay, fire)); return () => { }; };
            tube.PortraitFadeScheduler = (delay, fire) => { fades.Add((delay, fire)); return () => { }; };
            tube.SpeechAudioPlayer = (_, _, _, done) => done?.Invoke();
            tube.GigglePriority("voiced line", phraseAudioPath: Path.Combine(audio, "mapped.wav"), barkVoice: true, mood: "soft");
            Assert.Contains(Path.GetFileName(tube.PortraitPath), new[] { "happy1.png", "happy2.png" });
            Assert.Equal(TimeSpan.FromMilliseconds(152), Assert.Single(fades).Delay);
            Assert.NotNull(tube.FindControl<Image>("ImgAvatar")!.Source); Assert.NotNull(tube.FindControl<Image>("ImgAvatarB")!.Source);
            var first = tube.PortraitPath;
            Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(steps).Delay);
            SelectNow(tube, 2); Assert.Equal(Path.Combine(audio, "b", Path.GetFileName(first)), tube.PortraitPath);
            steps[0].Fire(); Assert.Equal(Path.Combine(audio, "b"), Path.GetDirectoryName(tube.PortraitPath));
            Assert.NotEqual(Path.GetFileName(first), Path.GetFileName(tube.PortraitPath));
            Assert.Equal(TimeSpan.FromSeconds(1), steps[1].Delay);
            steps[1].Fire();
            Assert.Equal(Path.Combine(audio, "b", Path.GetFileName(first)), tube.PortraitPath);
            Assert.Equal(TimeSpan.FromSeconds(2), steps[2].Delay);
            steps[2].Fire();
            Assert.Equal("idle.png", Path.GetFileName(tube.PortraitPath));
            tube.EndSpeechBubble();
            Action? queued = null;
            tube.SpeechDelayScheduler = (_, fire) => { queued = fire; return () => { }; };
            tube.Giggle("queued", phraseAudioPath: Path.Combine(audio, "mapped.mp3"), barkVoice: true, mood: "soft");
            queued!();
            Assert.Contains(Path.GetFileName(tube.PortraitPath), new[] { "happy1.png", "happy2.png" });
            tube.GigglePriority("mood", playSound: false, aiGenerated: false, mood: "unknown, soft");
            Assert.Equal("tender.png", Path.GetFileName(tube.PortraitPath));
            var (staleStep, staleFade) = (steps[^1].Fire, fades[^1].Fire);
            change(new ModPackage(new ModManifest { Id = "other" }, null, false));
            staleStep(); staleFade();
            Assert.False(tube.IsPortraitMode); Assert.Null(tube.FindControl<Image>("ImgAvatarB")!.Source);
            File.Delete(Path.Combine(audio, "a", "idle.png")); File.Delete(Path.Combine(audio, "b", "idle.png"));
            var previous = tube.FindControl<Image>("ImgAvatar")!.Source; Assert.NotNull(previous); Assert.True(tube.IsPoseRotating);
            change(new ModPackage(new ModManifest { Id = "portrait-no-idle" }, root, false));
            Assert.True(tube.IsPortraitMode); Assert.Null(tube.PortraitPath); Assert.False(tube.IsPoseRotating);
            Assert.Same(previous, tube.FindControl<Image>("ImgAvatar")!.Source);
            Assert.True(tube.RefreshRainPlayback().IsCompletedSuccessfully); Assert.False(tube.IsPoseRotating);
            tube.GigglePriority("mood", playSound: false, aiGenerated: false, mood: "soft");
            Assert.Equal("tender.png", Path.GetFileName(tube.PortraitPath));
            var emotionSource = tube.FindControl<Image>("ImgAvatarB")!.Source; Assert.NotNull(emotionSource); change(new ModPackage(new ModManifest { Id = "portrait-no-idle" }, root, false)); Assert.Same(emotionSource, tube.FindControl<Image>("ImgAvatar")!.Source);
        }
        finally { Directory.Delete(root, true); }
    });

    // A temp mod folder with the portrait manifest and a pose copy for every skin and file.
    private static (string Root, string Audio, ModPackage Mod) PortraitFixture(string id, string manifest, string[] skins, params string[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-" + id + "-" + Guid.NewGuid().ToString("N"));
        var audio = Path.Combine(root, "resources", "sounds", "companion_audio");
        Directory.CreateDirectory(audio);
        File.WriteAllText(Path.Combine(audio, "avatar_manifest.json"), manifest);
        foreach (var skin in skins) foreach (var name in files) CopyPose(audio, skin, name);
        return (root, audio, new ModPackage(new ModManifest { Id = id }, root, false));
    }

    private static void CopyPose(string audio, string skin, string name) => File.Copy(Path.Combine(ConditioningControlPanel.Tests.SourceRoots.RepoRoot,
        "Assets", "avatar_pose1.png"), Path.Combine(Directory.CreateDirectory(Path.Combine(audio, skin)).FullName, name));

    private static (string Root, string Audio, ModPackage Mod) PortraitReviewFixture() => PortraitFixture("portrait-review", """
        {"idleEmotion":"neutral","skins":[{"id":"a","dir":"a"},{"id":"b","dir":"b"}],"emotions":{"neutral":{"portraits":[{"file":"idle.png"}]},"happy":{"portraits":[{"file":"happy.png"}]}}}
        """, new[] { "a", "b" }, "idle.png", "happy.png");

    private static void SelectNow(AvatarTubeWindow tube, int set)
    {
        Action? finish = null;
        tube.AvatarSwitchScheduler = (_, fire) => { finish = fire; return () => { }; };
        Assert.True(tube.SelectAvatarSet(set)); finish!();
    }

    [Fact]
    public Task PortraitChromeShowsMistAndRestoresEffects() => Run((_, tube, change) =>
    {
        var (root, unusedAudio, mod) = PortraitReviewFixture();
        try
        {
            var a = tube.FindControl<Image>("ImgAvatar")!;
            var b = tube.FindControl<Image>("ImgAvatarB")!;
            var mist = tube.FindControl<Control>("MistOverlay")!;
            var effectA = a.Effect; var effectB = b.Effect;
            Assert.NotNull(effectA); Assert.NotNull(effectB); Assert.False(mist.IsVisible);
            change(mod);
            Assert.Null(a.Effect); Assert.Null(b.Effect); Assert.True(mist.IsVisible);
            SelectNow(tube, 2);
            Assert.Null(a.Effect); Assert.Null(b.Effect); Assert.True(mist.IsVisible);
            change(new ModPackage(new ModManifest { Id = "other" }, null, false));
            Assert.Same(effectA, a.Effect); Assert.Same(effectB, b.Effect); Assert.False(mist.IsVisible);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task PortraitCorruptImagesFallBackToSkinZeroThenIdle() => Run((_, tube, change) =>
    {
        var (root, audio, mod) = PortraitReviewFixture();
        try
        {
            foreach (var name in new[] { "idle.png", "happy.png" }) File.WriteAllText(Path.Combine(audio, "b", name), "corrupt");
            change(mod); SelectNow(tube, 2);
            Assert.Equal(Path.Combine(audio, "a", "idle.png"), tube.PortraitPath); tube.GigglePriority("line", playSound: false, aiGenerated: false, mood: "happy");
            Assert.Equal(Path.Combine(audio, "a", "happy.png"), tube.PortraitPath);
            File.WriteAllText(Path.Combine(audio, "a", "happy.png"), "corrupt"); change(mod); // reload the verified image cache
            tube.GigglePriority("line", playSound: false, aiGenerated: false, mood: "happy");
            Assert.Equal(Path.Combine(audio, "a", "idle.png"), tube.PortraitPath);
            File.Copy(Path.Combine(audio, "a", "idle.png"), Path.Combine(audio, "b", "idle.png"), true); change(mod);
            Assert.Equal(Path.Combine(audio, "b", "idle.png"), tube.PortraitPath); tube.GigglePriority("line", playSound: false, aiGenerated: false, mood: "happy");
            Assert.Equal(Path.Combine(audio, "a", "idle.png"), tube.PortraitPath);
        }
        finally { Directory.Delete(root, true); }
    });

    [Fact]
    public Task PortraitHeldIdleUsesTwoPosesAtNormalCadence() => Run((_, tube, change) =>
    {
        var (root, unusedAudio, mod) = PortraitReviewFixture();
        try
        {
            change(mod);
            var steps = new List<(TimeSpan Delay, Action Fire)>();
            tube.PortraitStepScheduler = (delay, fire) => { steps.Add((delay, fire)); return () => { }; };
            tube.PortraitFadeScheduler = (_, _) => () => { };
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Assert.False(AvatarTubeWindow.MayVoice(true));
            foreach (var text in new[] { "short", new string('a', 200).Replace("a", "a ") })
            {
                steps.Clear();
                tube.GigglePriority(text, playSound: false, aiGenerated: false, mood: "happy", unprompted: true);
                Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(steps).Delay);
                steps[0].Fire();
                Assert.Equal(TimeSpan.FromSeconds(2), steps[1].Delay);
                steps[1].Fire();
                Assert.Equal(2, steps.Count);
                Assert.Equal("idle.png", Path.GetFileName(tube.PortraitPath));
            }
            steps.Clear();
            tube.GigglePriority("short", playSound: false, aiGenerated: false, mood: "happy");
            Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(steps).Delay);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); Directory.Delete(root, true); }
    });

    [Fact]
    public Task AttachedTubeMakesRoomAndDetachRestoresTheShell() => Run((shell, tube, _) =>
    {
        tube.Detach();
        var area = shell.Screens.ScreenFromWindow(shell)!.WorkingArea;
        shell.Position = area.Position;
        shell.Width = area.Width / shell.DesktopScaling;
        shell.Height = 700;
        Dispatcher.UIThread.RunJobs();
        var width = shell.Width;
        var position = shell.Position;
        tube.Attach();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Position.X > position.X); // left dock: main moves right...
        Assert.True(shell.Width < width);            // ...and narrows (TubePlacement.cs MakeRoomPlacement)
        tube.Detach();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(position, shell.Position);
        Assert.Equal(width, shell.Width, 1);
    });

    [Fact]
    public Task UserMoveAfterMakeRoomWinsOverRestore() => Run((shell, tube, _) =>
    {
        tube.Detach();
        var area = shell.Screens.ScreenFromWindow(shell)!.WorkingArea;
        shell.Position = area.Position;
        shell.Width = area.Width / shell.DesktopScaling;
        Dispatcher.UIThread.RunJobs();
        var before = shell.Position;
        tube.Attach();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Position.X > before.X); // make-room moved the shell first
        shell.Position += new PixelVector(25, 0);
        Dispatcher.UIThread.RunJobs();
        var wanted = shell.Position;
        var width = shell.Width;
        tube.Detach();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(wanted, shell.Position);
        Assert.Equal(width, shell.Width);
    });

    [Fact]
    public Task ShowingTheTubeAgainRearmsMakeRoomAfterAUserTakeover() => Run((shell, tube, _) =>
    {
        tube.Detach();
        var area = shell.Screens.ScreenFromWindow(shell)!.WorkingArea;
        var fullWidth = area.Width / shell.DesktopScaling;
        shell.Position = area.Position;
        shell.Width = fullWidth;
        Dispatcher.UIThread.RunJobs();
        tube.Attach();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Position.X > area.X);
        // The user drags main back over the tube's spot: make-room stands down (WPF Windowing.cs:897-900).
        shell.Position = area.Position;
        shell.Width = fullWidth;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(area.Position, shell.Position);
        tube.Hide();
        Dispatcher.UIThread.RunJobs();
        tube.Show(); // WPF Speech.cs:1174 re-arms on show
        Dispatcher.UIThread.RunJobs();
        shell.Height += 1; // any parent change re-runs placement
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.Position.X > area.X);
    });

    [Fact]
    public Task BlankCustomLabelFallsBackToTheCompanionOrLegacyTitle() => Run((_, _, change) =>
    {
        var expected = AvatarTubeWindow.AvatarSetTitle(1);
        Assert.False(string.IsNullOrWhiteSpace(expected));
        change(new ModPackage(new ModManifest
        {
            Id = "tube-test",
            CustomAvatarSets = new() { new() { SetNumber = 1, Label = " " } },
        }, null, false));
        Assert.Equal(expected, AvatarTubeWindow.AvatarSetTitle(1)); // WPF Avatar.cs:938
    });

    [Fact]
    public Task DetachedZoomUsesCtrlWheelAndArrowKeysAndStopsAtLimits() => Run((_, tube, _) =>
    {
        tube.MouseWheel(new Point(300, 400), new Vector(0, 1), RawInputModifiers.Control);
        Assert.Equal(1, tube.CurrentScale); // attached
        tube.Detach();
        tube.MouseWheel(new Point(300, 400), new Vector(0, 1), RawInputModifiers.None);
        Assert.Equal(1, tube.CurrentScale); // requires Ctrl
        tube.MouseWheel(new Point(300, 400), new Vector(0, 1), RawInputModifiers.Control);
        Assert.Equal(1.25, tube.CurrentScale);
        tube.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        Assert.Equal(1.5, tube.CurrentScale);
        tube.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
        Assert.Equal(1.5, tube.CurrentScale);
        for (int i = 0; i < 5; i++)
            tube.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
        Assert.Equal(0.5, tube.CurrentScale);
        Assert.Equal(0.5, CoreSettings.Current.AvatarTubeScale);
        tube.Attach();
        Assert.Equal(1, tube.CurrentScale);
    });
}
