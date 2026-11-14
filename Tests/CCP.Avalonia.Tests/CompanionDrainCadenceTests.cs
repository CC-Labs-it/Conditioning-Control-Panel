using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CompanionDrainCadenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PerkAndCompanionChangesRestartOrStopDrain(int cleanupCheck) => AvaloniaTestDispatcher.Run(() =>
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        Func<CompanionId?> provider = () => CompanionId.BrainParasite;
        Action<CompanionId> switcher = _ => { };
        try
        {
            CoreModsHooks.ActiveCompanionProvider = provider;
            CoreModsHooks.SwitchCompanion = switcher;
            var service = RunScenario(path);
            if (cleanupCheck == 0) Assert.Same(provider, CoreModsHooks.ActiveCompanionProvider);
            else if (cleanupCheck == 1) Assert.Same(switcher, CoreModsHooks.SwitchCompanion);
            else if (cleanupCheck == 2) Assert.False(service.HasPendingSave);
            else if (originalFile == null) Assert.False(File.Exists(path));
            else Assert.Equal(originalFile, File.ReadAllBytes(path));
        }
        finally
        {
            (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
        }
    });

    private static readonly FieldInfo LastActiveTime = typeof(CompanionCore).GetField("_lastActiveTimeUpdate", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo LastDrainSave = typeof(CompanionCore).GetField("_lastDrainSaveUtc", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ScenariosRestoreActiveTime(bool worker, bool started) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldTime = LastActiveTime.GetValue(null);
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var oldProvider = CoreSettings.ServiceProvider;
        var oldEnv = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var wasStarted = CompanionHead.Started;
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = 600_000 };
        var sentinel = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Local);
        try
        {
            if (wasStarted) CompanionHead.Stop();
            CoreSettings.ServiceProvider = () => service;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "0");
            if (started) CompanionHead.Start();
            LastActiveTime.SetValue(null, sentinel);
            if (worker) await RunWorkerSwitchScenario();
            else RunScenario(Path.Combine(CorePaths.UserData, "settings.json"));
            Assert.Equal(sentinel, LastActiveTime.GetValue(null));
        }
        finally
        {
            CompanionHead.Stop();
            service.SealForReset();
            CoreSettings.ServiceProvider = oldProvider;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", oldEnv);
            try { if (wasStarted) CompanionHead.Start(); }
            finally
            {
                LastActiveTime.SetValue(null, oldTime);
                (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
            }
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RestartedHeadStillSwitchesAfterCleanup(bool worker) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var oldProvider = CoreSettings.ServiceProvider;
        var oldActiveTime = LastActiveTime.GetValue(null);
        var wasStarted = CompanionHead.Started;
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = 600_000 };
        try
        {
            if (wasStarted) CompanionHead.Stop();
            CoreSettings.ServiceProvider = () => service;
            CompanionHead.Start();
            var hook = CoreModsHooks.SwitchCompanion;
            if (worker) await RunWorkerSwitchScenario();
            else RunScenario(path);
            Assert.True(CompanionHead.Started);
            Assert.Same(hook, CoreModsHooks.SwitchCompanion);
            var next = CompanionCore.ActiveCompanion == CompanionId.CultBunny ? CompanionId.OGBambiSprite : CompanionId.CultBunny;
            await Task.Run(() => CoreModsHooks.SwitchCompanion!(next));
            Assert.Equal(next, CompanionCore.ActiveCompanion);
        }
        finally
        {
            CompanionHead.Stop();
            service.SealForReset();
            try
            {
                if (originalFile == null) File.Delete(path);
                else File.WriteAllBytes(path, originalFile);
            }
            finally
            {
                CoreSettings.ServiceProvider = oldProvider;
                try { if (wasStarted) CompanionHead.Start(); }
                finally
                {
                    LastActiveTime.SetValue(null, oldActiveTime);
                    (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
                }
            }
        }
    });

    [Fact]
    public async Task WorkerSwitchKeepsDrainOnUiAndDoesNotRestartANewHead()
    {
        var original = LastDrainSave.GetValue(null);
        var sentinel = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        try
        {
            LastDrainSave.SetValue(null, sentinel);
            await RunWorkerSwitchScenario();
            Assert.Equal(sentinel, LastDrainSave.GetValue(null));
        }
        finally { LastDrainSave.SetValue(null, original); }
    }

    private static Task RunWorkerSwitchScenario() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var oldEnv = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var oldProvider = CoreSettings.ServiceProvider;
        var oldActiveTime = LastActiveTime.GetValue(null);
        var oldDrainSave = LastDrainSave.GetValue(null);
        var wasStarted = CompanionHead.Started;
        SettingsService? service = null;
        var drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDrain(double _) => drained.TrySetResult(Dispatcher.UIThread.CheckAccess());
        try
        {
            if (wasStarted) CompanionHead.Stop();
            service = new SettingsService { SaveDebounceDueTimeMilliseconds = 600_000 };
            CoreSettings.ServiceProvider = () => service;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "1");
            var s = service.Current;
            s.PlayerLevel = 1000;
            s.PlayerXP = 50;
            s.CompanionPerk = CompanionBonusType.XPDrain;
            s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
            s.SetCompanionPromptId((int)CompanionId.CultBunny, null);
            s.SetCompanionPromptId((int)CompanionId.BrainParasite, null);
            CompanionCore.XPDrained += OnDrain;
            CompanionHead.Start();
            await Task.Run(() => CoreModsHooks.SwitchCompanion!(CompanionId.CultBunny));
            Assert.True(await drained.Task.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal(50 - CompanionCore.DrainXpPerTick, s.PlayerXP);

            // Hold the UI thread until the worker has queued its update, then replace the head.
            Task.Run(() => CoreModsHooks.SwitchCompanion!(CompanionId.BrainParasite)).GetAwaiter().GetResult();
            CompanionHead.Stop();
            CompanionHead.Start();
            var newTimer = CompanionHead.DrainTimer;
            Assert.NotNull(newTimer);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(newTimer, CompanionHead.DrainTimer);
        }
        finally
        {
            CompanionCore.XPDrained -= OnDrain;
            try { CompanionHead.Stop(); }
            finally { LastDrainSave.SetValue(null, oldDrainSave); }
            service?.SealForReset();
            try
            {
                if (originalFile == null) File.Delete(path);
                else File.WriteAllBytes(path, originalFile);
            }
            finally
            {
                CoreSettings.ServiceProvider = oldProvider;
                Environment.SetEnvironmentVariable("CCP_COMPANION_V2", oldEnv);
                try { if (wasStarted) CompanionHead.Start(); }
                finally
                {
                    LastActiveTime.SetValue(null, oldActiveTime);
                    (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
                }
            }
        }
    });

    private static SettingsService RunScenario(string path)
    {
        var originalFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var oldHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var oldEnv = Environment.GetEnvironmentVariable("CCP_COMPANION_V2");
        var oldProvider = CoreSettings.ServiceProvider;
        var oldActiveTime = LastActiveTime.GetValue(null);
        var wasStarted = CompanionHead.Started;
        SettingsService? service = null;
        try
        {
            if (wasStarted) CompanionHead.Stop();
            service = new SettingsService { SaveDebounceDueTimeMilliseconds = 600_000 };
            CoreSettings.ServiceProvider = () => service;
            Environment.SetEnvironmentVariable("CCP_COMPANION_V2", "1");
            var s = CoreSettings.Current;
            s.PlayerLevel = 1000;
            s.CompanionPerk = CompanionBonusType.PinkFilterBonus;
            CompanionHead.Start();
            Assert.Null(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var first = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.XPDrain));
            var second = Assert.IsAssignableFrom<IDisposable>(CompanionHead.DrainTimer);
            Assert.NotSame(first, second);
            var next = CompanionCore.ActiveCompanion == CompanionId.CultBunny ? CompanionId.OGBambiSprite : CompanionId.CultBunny;
            CompanionCore.SwitchCompanion(next);
            Assert.NotSame(second, CompanionHead.DrainTimer);
            Assert.True(CompanionHead.SetPerk(CompanionBonusType.PinkFilterBonus));
            Assert.Null(CompanionHead.DrainTimer);
            Assert.True(service.HasPendingSave);
            // Exercise file restoration even when the debounce has not fired.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "cadence test fixture");
            return service;
        }
        finally
        {
            CompanionHead.Stop();
            service?.SealForReset();
            try
            {
                if (originalFile == null) File.Delete(path);
                else File.WriteAllBytes(path, originalFile);
            }
            finally
            {
                CoreSettings.ServiceProvider = oldProvider;
                Environment.SetEnvironmentVariable("CCP_COMPANION_V2", oldEnv);
                try { if (wasStarted) CompanionHead.Start(); }
                finally
                {
                    LastActiveTime.SetValue(null, oldActiveTime);
                    (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = oldHooks;
                }
            }
        }
    }
}
