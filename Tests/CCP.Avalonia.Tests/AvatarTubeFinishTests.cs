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
