using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Services;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ModSwitchRetiresOnlyDtrhWithWpfGracePeriod(bool ready, bool acknowledge) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
            if (Application.Current is null)
                builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            else builder.AfterSetupCallback(builder);
            var priorSettings = CoreSettings.ServiceProvider;
            var priorAppMods = App.Mods;
            var priorService = CoreMods.Service;
            var priorProvider = CoreMods.ActiveModIdProvider;
            var priorHook = CoreModsHooks.CloseDtrhHost;
            var mods = new CoreModsSnapshot();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            try
            {
                CoreModsHooks.CloseDtrhHost = null;
                App.StartMods();
                var next = App.Mods!.ActiveModId == BuiltInMods.SissyHypnoId
                    ? BuiltInMods.BambiSleepId : BuiltInMods.SissyHypnoId;
                var host = new GameWindow(GameWindow.Games["dtrh"]);
                var other = new GameWindow(GameWindow.Games["breakoutdemo"]);
                host.Show();
                other.Show();
                var posted = new List<string>();
                host.Posted += posted.Add;
                if (ready) host.HandleMessage("{\"type\":\"ready\"}");
                posted.Clear();
                App.Mods.ActivateMod(next);
                if (ready)
                {
                    Assert.Contains(posted, p => p.Contains("end-run") && p.Contains("host"));
                    Assert.True(host.IsVisible);
                    Assert.False(host.DtrhReady);
                    if (acknowledge) host.HandleMessage("{\"type\":\"exit-done\"}");
                    else
                    {
                        var clock = Stopwatch.StartNew();
                        while (host.IsVisible && clock.Elapsed < TimeSpan.FromSeconds(3))
                        {
                            await Task.Delay(20);
                        }
                        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(1100));
                    }
                }
                Assert.False(host.IsVisible);
                Assert.True(other.IsVisible);
                Assert.DoesNotContain(posted, p => p.Contains("end-run") && !ready);
            }
            finally
            {
                try
                {
                    GameWindow.CloseAllForPanic();
                    settings.SealForReset();
                }
                finally
                {
                    CoreSettings.ServiceProvider = priorSettings;
                    mods.Dispose();
                }
            }
            Assert.Same(priorSettings, CoreSettings.ServiceProvider);
            Assert.Same(priorAppMods, App.Mods);
            Assert.Same(priorService, CoreMods.Service);
            Assert.Same(priorProvider, CoreMods.ActiveModIdProvider);
            Assert.Same(priorHook, CoreModsHooks.CloseDtrhHost);
        });

    [Theory]
    [InlineData("app")]
    [InlineData("service")]
    [InlineData("provider")]
    [InlineData("hook")]
    public void ModSnapshotRestoresPriorServicesProvidersAndDtrhHook(string member)
    {
        using var outer = new CoreModsSnapshot();
        var appMods = typeof(App).GetProperty("Mods", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = new ModService();
        CoreMods.Attach(original);
        appMods.SetValue(null, original);
        var provider = CoreMods.ActiveModIdProvider;
        Action hook = () => { };
        CoreModsHooks.CloseDtrhHost = hook;
        var snapshot = new CoreModsSnapshot();
        try
        {
            var replacement = new ModService();
            CoreMods.Attach(replacement);
            appMods.SetValue(null, replacement);
            CoreModsHooks.CloseDtrhHost = () => { };
        }
        finally { snapshot.Dispose(); }
        switch (member)
        {
            case "app": Assert.Same(original, App.Mods); break;
            case "service": Assert.Same(original, CoreMods.Service); break;
            case "provider": Assert.Same(provider, CoreMods.ActiveModIdProvider); break;
            case "hook": Assert.Same(hook, CoreModsHooks.CloseDtrhHost); break;
            default: throw new ArgumentOutOfRangeException(nameof(member));
        }
    }
}
