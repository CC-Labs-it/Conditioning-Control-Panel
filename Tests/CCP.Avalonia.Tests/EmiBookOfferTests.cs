using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class EmiBookOfferTests
{
    private sealed class FakeStore : TutorialService.ITourCompletionStore
    {
        public readonly HashSet<string> Done = new();
        public bool Has(string name) => Done.Contains(name);
        public void Latch(string name) => Done.Add(name);
    }

    [Fact]
    public Task EmiScopeRestoresStateEngineAndPendingSave() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var original = EmiState.Current;
        EmiState.SaveNow();
        var path = EmiState.FilePath;
        var before = File.ReadAllBytes(path);
        var engine = EmiLineEngine.Instance;
        var fields = new[] { "_file", "_bags", "_rng", "_volatileLimits", "_lastSpokeUtc" };
        var saved = Array.ConvertAll(fields, name => typeof(EmiLineEngine)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        using (new EmiTestScope())
        {
            Assert.NotSame(original, EmiState.Current);
            Assert.Equal(1, EmiState.NoteSummon());
            Assert.True(engine.Ready);
            engine.Draw("bookOffer");
            Assert.True(engine.EverSpent("bookOffer"));
            EmiState.SaveSoon(); // leave a pending write to exercise scope teardown
        }
        Assert.Same(original, EmiState.Current);
        for (int i = 0; i < fields.Length; i++)
        {
            var restored = typeof(EmiLineEngine).GetField(fields[i], BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
            if (i == fields.Length - 1) Assert.Equal(saved[i], restored);
            else Assert.Same(saved[i], restored);
        }
        Assert.Null(typeof(EmiState).GetField("_saveTimer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
        await Task.Delay(750);
        Assert.Equal(before, File.ReadAllBytes(path));
    });

    [Fact]
    public Task SummonTestLeavesCallerStateUntouched() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var state = EmiState.Current;
        EmiState.SaveNow();
        var before = Newtonsoft.Json.JsonConvert.SerializeObject(state);
        var file = File.ReadAllBytes(EmiState.FilePath);
        var engine = EmiLineEngine.Instance;
        var bags = typeof(EmiLineEngine).GetField("_bags", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
        var random = typeof(EmiLineEngine).GetField("_rng", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine);
        await EndingOffersOnce(false, "tourSkipped");
        Assert.Same(state, EmiState.Current);
        Assert.Equal(before, Newtonsoft.Json.JsonConvert.SerializeObject(state));
        Assert.Same(bags, typeof(EmiLineEngine).GetField("_bags", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        Assert.Same(random, typeof(EmiLineEngine).GetField("_rng", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine));
        await Task.Delay(750);
        Assert.Equal(file, File.ReadAllBytes(EmiState.FilePath));
    });

    [Theory]
    [InlineData(true, "tourFinished")]
    [InlineData(false, "tourSkipped")]
    public Task EndingOffersOnce(bool completed, string why) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var emi = new EmiTestScope();
        var events = new List<EmiMoment>();
        void OnMoment(object? sender, EmiMoment e) { if (e.Id == "bookOffer") events.Add(e); }
        var desk = EmiDeskService.Instance;
        var content = EmiBookOffer.HasContent; var open = EmiBookOffer.IsOpen; var opens = EmiBookOffer.Opens;
        var delay = EmiBookOffer.DelayMs;
        var previousStore = TutorialService.CompletionStore;
        var store = new FakeStore();
        var previousSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        var previousShell = TutorialHeadHooks.ShellProvider;
        MainShellWindow? shell = null;
        bool deliveredWhileOut = false;
        void OnDelivery(object? sender, EmiMoment e) { if (e.Id == "bookOffer") deliveredWhileOut = desk.IsOut && desk.Window?.IsVisible == true; }
        try
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            CoreSettings.ServiceProvider = () => settings;
            CoreSettings.Current.PerformanceMode = true;
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            TutorialService.CompletionStore = store;
            shell = new MainShellWindow(); shell.Show();
            TutorialHeadHooks.ShellProvider = () => shell;
            Assert.False(desk.IsOut);
            desk.MomentFired += OnDelivery;
            EmiBookOffer.HasContent = () => true; EmiBookOffer.IsOpen = () => false; EmiBookOffer.Opens = () => 0;
            EmiBookOffer.DelayMs = 30; desk.MomentFired += OnMoment;
            TutorialHead.Seed(); TutorialHead.StartOnShell(TutorialType.ShortWalk);
            await TutorialHead.ShellStartup; // the trunk summons before the first card
            Assert.True(desk.IsOut);
            Assert.True(desk.Window?.IsVisible);
            Assert.True(TutorialHead.Service.IsActive);
            if (completed) while (TutorialHead.Service.IsActive) TutorialHead.Service.Next();
            else TutorialHead.Service.Skip();
            for (int i = 0; i < 40 && events.Count == 0; i++) await Task.Delay(50);
            var moment = Assert.Single(events);
            Assert.Equal(why, moment.Context!.GetType().GetProperty("why")!.GetValue(moment.Context));
            Assert.True(deliveredWhileOut);
            Assert.Equal(completed, store.Has(nameof(TutorialType.ShortWalk)));
        }
        finally
        {
            desk.MomentFired -= OnDelivery;
            desk.MomentFired -= OnMoment; TutorialHead.ResetForTests();
            desk.Window?.ShutDown(); shell?.Close(); Dispatcher.UIThread.RunJobs();
            TutorialHeadHooks.ShellProvider = previousShell;
            TutorialService.CompletionStore = previousStore;
            CoreSettings.ServiceProvider = previousSettings;
            EmiBookOffer.HasContent = content; EmiBookOffer.IsOpen = open; EmiBookOffer.Opens = opens; EmiBookOffer.DelayMs = delay;
        }
    });

    [Fact]
    public Task OtherWindowTourDoesNotScheduleAnOffer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var emi = new EmiTestScope();
        var events = new List<EmiMoment>();
        void OnMoment(object? sender, EmiMoment e) { if (e.Id == "bookOffer") events.Add(e); }
        var desk = EmiDeskService.Instance;
        var content = EmiBookOffer.HasContent; var open = EmiBookOffer.IsOpen; var opens = EmiBookOffer.Opens;
        var delay = EmiBookOffer.DelayMs;
        try
        {
            EmiBookOffer.HasContent = () => true; EmiBookOffer.IsOpen = () => false; EmiBookOffer.Opens = () => 0;
            EmiBookOffer.DelayMs = 30; desk.MomentFired += OnMoment;
            TutorialHead.Seed(); TutorialHead.StartByName(nameof(TutorialType.Modding));
            Assert.True(TutorialHead.Service.IsActive);
            TutorialHead.Service.Skip(); await Task.Delay(150);
            Assert.Empty(events);
        }
        finally
        {
            desk.MomentFired -= OnMoment; TutorialHead.ResetForTests();
            EmiBookOffer.HasContent = content; EmiBookOffer.IsOpen = open; EmiBookOffer.Opens = opens; EmiBookOffer.DelayMs = delay;
        }
    });

    [Fact]
    public Task RearmBrakesAndStop() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var emi = new EmiTestScope();
        var events = new List<EmiMoment>();
        void OnMoment(object? sender, EmiMoment e) { if (e.Id == "bookOffer") events.Add(e); }
        var desk = EmiDeskService.Instance;
        var content = EmiBookOffer.HasContent; var open = EmiBookOffer.IsOpen; var opens = EmiBookOffer.Opens;
        var delay = EmiBookOffer.DelayMs;
        try
        {
            EmiBookOffer.HasContent = () => true; EmiBookOffer.IsOpen = () => false; EmiBookOffer.Opens = () => 0;
            EmiBookOffer.DelayMs = 80; desk.MomentFired += OnMoment;
            EmiBookOffer.Schedule("tourFinished"); await Task.Delay(40); EmiBookOffer.Schedule("tourSkipped");
            await Task.Delay(160);
            var moment = Assert.Single(events);
            Assert.Equal("tourSkipped", moment.Context!.GetType().GetProperty("why")!.GetValue(moment.Context));
            events.Clear(); EmiBookOffer.HasContent = () => false; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            EmiBookOffer.HasContent = () => true; EmiBookOffer.Opens = () => 1; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            EmiBookOffer.Opens = () => 0; EmiBookOffer.IsOpen = () => true; EmiBookOffer.Schedule("tourFinished"); await Task.Delay(120);
            Assert.Empty(events);
            // App exit and the test reset cancel a pending offer. Panic does not: WPF EmiCodex.MaybeOfferSoon
            // keeps its timer and the line engine's panic silence holds the line.
            EmiBookOffer.IsOpen = () => false; EmiBookOffer.Schedule("tourFinished"); EmiBookOffer.Stop(); await Task.Delay(120);
            Assert.Empty(events);
        }
        finally
        {
            desk.MomentFired -= OnMoment; TutorialHead.ResetForTests();
            EmiBookOffer.HasContent = content; EmiBookOffer.IsOpen = open; EmiBookOffer.Opens = opens; EmiBookOffer.DelayMs = delay;
        }
    });
}
