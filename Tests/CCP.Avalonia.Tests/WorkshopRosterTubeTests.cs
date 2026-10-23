using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopRosterTubeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 5)]
    [InlineData(3, 6)]
    [InlineData(4, 7)]
    public Task SwitchPersistsAndRendersCompanionAtPlayerLevelOne(int companion, int set) =>
        WithTube(async tube =>
        {
            CoreSettings.Current.PlayerLevel = 1; // WPF removed level gating
            CoreSettings.Current.CompanionProgressData[companion] = new CompanionProgress { Level = 8 };
            await Settle(tube.SwitchToCompanionAvatarAsync((CompanionId)companion));
            Assert.Equal(set, tube.CurrentAvatarSet);
            Assert.Equal(set, CoreSettings.Current.SelectedAvatarSet);
            Assert.Equal(set, CoreSettings.Current.ModAvatarSet["roster-static-test"]);
            Assert.True(tube.FindControl<TextBlock>("TxtAvatarLevel")!.IsVisible);
            Assert.Contains("8", tube.FindControl<TextBlock>("TxtAvatarLevel")!.Text);
            Assert.Equal(Title(companion), tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
            Assert.NotNull(tube.FindControl<Image>("ImgAvatar")!.Source);
            Assert.Equal(1, tube.FindControl<Border>("AvatarBorder")!.Opacity);
        });

    [Fact]
    public Task RapidSwitchKeepsLatestAndPanicAndCloseCancelPendingPaint() => WithTube(async tube =>
    {
        var oldTitle = tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text;
        var first = tube.SwitchToCompanionAvatarAsync((CompanionId)1);
        Assert.False(first.IsCompleted);
        Assert.Equal(oldTitle, tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text); // swap follows fade-out
        var latest = tube.SwitchToCompanionAvatarAsync((CompanionId)4);
        await Settle(Task.WhenAll(first, latest));
        Assert.Equal(7, tube.CurrentAvatarSet);
        var title = tube.FindControl<TextBlock>("TxtAvatarTitle")!;
        var settled = title.Text;
        var pending = tube.SwitchToCompanionAvatarAsync((CompanionId)2);
        tube.PanicSilence(); // existing tube-speech panic callback also cancels the finite fade
        await Settle(pending);
        Assert.Equal(5, tube.CurrentAvatarSet); // panic skips the fade, not the saved switch
        Assert.Equal(5, CoreSettings.Current.SelectedAvatarSet);
        Assert.Equal(Title(2), title.Text);
        Assert.NotEqual(settled, title.Text);
        settled = title.Text;
        Assert.Equal(1, tube.FindControl<Border>("AvatarBorder")!.Opacity);
        pending = tube.SwitchToCompanionAvatarAsync((CompanionId)3);
        tube.Close();
        await Settle(pending);
        Assert.Equal(settled, title.Text);
        var closedSet = tube.CurrentAvatarSet;
        await Settle(tube.SwitchToCompanionAvatarAsync((CompanionId)0));
        Assert.Equal(closedSet, tube.CurrentAvatarSet);
    });

    [Fact]
    public Task WorkerThreadSwitchMarshalsToTubeDispatcher() => WithTube(async tube =>
    {
        await Settle(Task.Run(() => tube.SwitchToCompanionAvatarAsync((CompanionId)2)));
        Assert.Equal(5, tube.CurrentAvatarSet);
        Assert.Equal(1, tube.FindControl<Border>("AvatarBorder")!.Opacity);
    });

    [Fact]
    public Task SingleEmotePinPreservesLookButAllowsPersonaSwitch() => WithTube(async tube =>
    {
        CoreMods.ActiveModIdProvider = () => BuiltInMods.CCPDefaultId;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(BuiltInMods.CCPDefault, null, true);
        Assert.True(AvatarTubeWindow.IsSingleEmoteAvatarMod());
        var before = tube.CurrentAvatarSet;
        var saved = CoreSettings.Current.SelectedAvatarSet;
        var title = tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text;
        CompanionCore.SwitchCompanion((CompanionId)2);
        await Settle(tube.SwitchToCompanionAvatarAsync((CompanionId)2));
        Assert.Equal(2, CoreSettings.Current.ActiveCompanionId);
        Assert.Equal(before, tube.CurrentAvatarSet);
        Assert.Equal(saved, CoreSettings.Current.SelectedAvatarSet);
        Assert.Equal(title, tube.FindControl<TextBlock>("TxtAvatarTitle")!.Text);
        Assert.False(CoreSettings.Current.ModAvatarSet.ContainsKey(BuiltInMods.CCPDefaultId));
    });

    [Fact]
    public Task PinUsesRegistryAndLocalFoldersAndDeclaredOtherSets() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldId = CoreMods.ActiveModIdProvider;
        var oldPackage = CoreMods.ActiveModPackageProvider;
        var directory = Path.Combine(Path.GetTempPath(), "ccp-roster-emotes-" + Guid.NewGuid().ToString("N"));
        var manifest = new ModManifest { Id = "roster-local-test" };
        CoreMods.ActiveModIdProvider = () => manifest.Id;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(manifest, directory, false);
        void AddSet(string name, bool hasMap)
        {
            var folder = Path.Combine(directory, "resources", "emotes", name);
            Directory.CreateDirectory(folder);
            if (hasMap) File.WriteAllText(Path.Combine(folder, "emotes.json"), "{}");
        }
        try
        {
            Assert.False(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            AddSet("set2", true);
            AddSet("set9", false); // folder alone does not count
            AddSet("setinvalid", true);
            Assert.True(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            manifest.SupportedAvatarSets = new() { 2 };
            Assert.True(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            manifest.SupportedAvatarSets.Add(3);
            Assert.False(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            manifest.SupportedAvatarSets = null;
            manifest.CustomAvatarSets = new() { new CustomAvatarSet { SetNumber = 4 } };
            Assert.False(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            manifest.CustomAvatarSets = null;
            AddSet("set3", true);
            Assert.False(AvatarTubeWindow.IsSingleEmoteAvatarMod());

            foreach (var mod in new[] { BuiltInMods.BambiSleepId, BuiltInMods.SissyHypnoId, BuiltInMods.CCPDefaultId })
            {
                CoreMods.ActiveModIdProvider = () => mod;
                CoreMods.ActiveModPackageProvider = () => null;
                Assert.True(AvatarTubeWindow.IsSingleEmoteAvatarMod());
            }
            CoreMods.ActiveModIdProvider = () => BuiltInMods.LockedId;
            Assert.False(AvatarTubeWindow.IsSingleEmoteAvatarMod()); // four registry entries
        }
        finally
        {
            CoreMods.ActiveModIdProvider = oldId;
            CoreMods.ActiveModPackageProvider = oldPackage;
            Directory.Delete(directory, true);
        }
        return Task.CompletedTask;
    });

    private static string Title(int companion) => CoreMods.MakeModAware(CompanionDefinition
        .GetById((CompanionId)companion).GetDisplayName(CoreSettings.Current.SlutModeEnabled)).ToUpperInvariant();

    // Headless animations only advance on a forced render tick; bound by time, not turns (P73).
    private static async Task Settle(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(task.IsCompleted, "avatar switch did not finish within 10 s");
        await task;
    }

    private static Task WithTube(Func<AvatarTubeWindow, Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldId = CoreMods.ActiveModIdProvider;
        var oldPackage = CoreMods.ActiveModPackageProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.SelectedAvatarSet = 1;
        CoreSettings.Current.ModAvatarSet.Clear();
        CoreMods.ActiveModIdProvider = () => "roster-static-test";
        CoreMods.ActiveModPackageProvider = () => null;
        var owner = new Window();
        var tube = new AvatarTubeWindow(owner);
        try
        {
            owner.Show();
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            await action(tube);
        }
        finally
        {
            tube.Close();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = oldSettings;
            CoreMods.ActiveModIdProvider = oldId;
            CoreMods.ActiveModPackageProvider = oldPackage;
        }
    });
}
