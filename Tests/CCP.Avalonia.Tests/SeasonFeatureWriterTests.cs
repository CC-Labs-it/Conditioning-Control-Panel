using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class SeasonFeatureWriterTests
{
    private static async Task WithLog(Func<FeatureDayLogService, Task> action)
    {
        var dir = Directory.CreateTempSubdirectory("season-writer-");
        var property = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldLog = property.GetValue(null);
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        typeof(SettingsService).GetProperty("Current")!.SetValue(service, new AppSettings());
        CoreSettings.ServiceProvider = () => service;
        using var log = new FeatureDayLogService(() => null, Path.Combine(dir.FullName, "day.json"));
        property.SetValue(null, log);
        try { await action(log); }
        finally { property.SetValue(null, oldLog); CoreSettings.ServiceProvider = oldProvider; log.Dispose(); dir.Delete(true); }
    }

    private static int Uses(FeatureDayLogService log, string key) =>
        log.BuildWirePayload(10).Sum(d => d.TryGetValue(key, out var n) ? Convert.ToInt32(n) : 0);

    [Fact]
    public Task Chaos() => AvaloniaTestDispatcher.RunAsync(() => WithLog(log =>
    {
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.Equal(1, Uses(log, "e_ch"));
            typeof(ChaosRunHost).GetMethod("RunAgain", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosRunHost.IsActive);
            Assert.Equal(1, Uses(log, "e_ch"));
        }
        finally { ChaosRunHost.ForceShutdown(); }
        return Task.CompletedTask;
    }));

    [Fact]
    public Task Dtrh() => AvaloniaTestDispatcher.RunAsync(() => WithLog(log =>
    {
        var w = new GameWindow(GameWindow.Games[GameWindow.DtrhId]);
        try { w.HandleMessage("{\"type\":\"run-started\"}"); Assert.Equal(1, Uses(log, "e_dt")); }
        finally { w.Close(); }
        return Task.CompletedTask;
    }));

    [Fact]
    public Task Race() => AvaloniaTestDispatcher.RunAsync(() => WithLog(log =>
    {
        // The 2D race window is "raceclassic" since b237 (#4514, as WPF): "race" is Racing Thoughts 3D (e_race3d).
        var w = new GameWindow(new GameWindow.Game("raceclassic", "launcher_game_race_title", "dtrh/race.html"));
        try
        {
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            Assert.Equal(1, Uses(log, "e_rc"));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    }));

    [Fact]
    public Task Quiz() => AvaloniaTestDispatcher.RunAsync(() => WithLog(log =>
    {
        try
        {
            PopQuizHost.Instance.Open(true);
            Assert.Equal(0, Uses(log, "e_pq"));
            PopQuizHost.Instance.CloseAll();
            PopQuizHost.Instance.Open(false);
            Assert.Equal(1, Uses(log, "e_pq"));
        }
        finally { PopQuizHost.Instance.CloseAll(); }
        return Task.CompletedTask;
    }));

    [Fact]
    public Task Companion() => AvaloniaTestDispatcher.RunAsync(() => WithLog(async log =>
    {
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            tube.FindControl<TextBox>("TxtUserInput")!.Text = "hello";
            await tube.SendChatAsync();
            Assert.Equal(1, Uses(log, "e_cp"));
        }
        finally { tube.Close(); Dispatcher.UIThread.RunJobs(); }
    }));
}
