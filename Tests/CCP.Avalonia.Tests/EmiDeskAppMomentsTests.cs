using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Speech;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskAppMomentsTests
{
    [Fact]
    public void ClockLatchesDaysNightsAndLongSittingOnlyWhileOut()
    {
        var desk = new EmiDeskService();
        var moments = new List<string>();
        var now = new DateTime(2026, 1, 1, 23, 59, 0);
        var utc = now;
        desk.ClockLocal = () => now;
        desk.ClockUtc = () => utc;
        desk.IdleSeconds = () => 0;
        desk.ClockMoment = (name, _) => moments.Add(name);
        var launched = utc;
        desk.ClockTick(launched);
        Assert.Empty(moments);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        desk.ClockTick(launched);
        now = now.AddMinutes(61);
        utc = utc.AddHours(2);
        desk.ClockTick(launched);
        desk.ClockTick(launched);
        Assert.Equal(1, moments.FindAll(x => x == "dayTurned").Count);
        Assert.Equal(1, moments.FindAll(x => x == "lateNight").Count);
        Assert.Equal(1, moments.FindAll(x => x == "longSitting").Count);
        Assert.Contains("ambient", moments);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, false);
        moments.Clear();
        now = now.AddDays(1);
        desk.ClockTick(launched);
        Assert.Empty(moments);
    }
    [Fact]
    public void SmallHoursAndMorningAreLatchedByNightAndDay()
    {
        var desk = new EmiDeskService();
        var seen = new List<string>();
        var now = new DateTime(2026, 1, 2, 3, 0, 0);
        desk.ClockLocal = () => now;
        desk.ClockUtc = () => now;
        desk.IdleSeconds = () => 0;
        desk.ClockMoment = (name, _) => seen.Add(name);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        desk.ClockTick(now); desk.ClockTick(now);
        now = now.AddHours(2);
        desk.ClockTick(now); desk.ClockTick(now);
        Assert.Single(seen.Where(x => x == "smallHours"));
        Assert.Single(seen.Where(x => x == "morningFirst"));
    }

    [Fact]
    public void ClockUsesIdleThresholdsAndRearmsLongIdleAfterInput()
    {
        var desk = new EmiDeskService();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        int idle = 0;
        var seen = new List<(string name, object? context)>();
        desk.ClockLocal = () => now;
        desk.ClockUtc = () => now;
        desk.IdleSeconds = () => idle;
        desk.ClockMoment = (name, context) => seen.Add((name, context));
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        void Tick(int seconds) { idle = seconds; desk.ClockTick(now); }
        Tick(299); Tick(300); Tick(1199); Tick(1200); Tick(1201); Tick(300); Tick(1200); Tick(299); Tick(1200);
        Assert.Equal(new[] { "ambient", "idleShort", "idleShort", "appIdleLong", "idleShort", "ambient", "appIdleLong" }, seen.Select(x => x.name));
        Assert.Equal(new[] { 5, 19, 20, 5, 20 }, seen.Where(x => x.context != null)
            .Select(x => (int)x.context!.GetType().GetProperty("minutes")!.GetValue(x.context)!));
        desk.IdleSeconds = () => throw new InvalidOperationException();
        seen.Clear(); desk.ClockTick(now);
        Assert.Equal("ambient", Assert.Single(seen).name);
    }

    private static List<string> IdleSequence(params int[] values) => IdleSequence(v => v, values);

    private static List<string> IdleSequence(Func<int, int> idleAt, params int[] values)
    {
        var desk = new EmiDeskService();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var seen = new List<string>();
        int idle = 0;
        desk.ClockLocal = () => now;
        desk.ClockUtc = () => now;
        desk.IdleSeconds = () => idle;
        desk.ClockMoment = (name, _) => seen.Add(name);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        foreach (var value in values) { idle = idleAt(value); desk.ClockTick(now); }
        return seen;
    }

    [Fact]
    public void IdleShortStartsAtFiveMinutes() => Assert.Equal(new[] { "ambient", "idleShort" }, IdleSequence(299, 300));

    [Fact]
    public void LongIdleStartsAtTwentyMinutesAndLatches() => Assert.Equal(new[] { "idleShort", "appIdleLong" }, IdleSequence(1199, 1200, 1201));

    [Fact]
    public void InputRearmsLongIdle() => Assert.Equal(new[] { "appIdleLong", "ambient", "appIdleLong" }, IdleSequence(1201, 1, 1201));

    [Fact]
    public void X11ProbeReturnsNoValueForUnavailableDisplay()
    {
        if (OperatingSystem.IsLinux()) Assert.Null(DesktopIdle.ReadX11("not-a-display"));
    }

    [Fact]
    public Task WaylandIdleFlowsThroughClockAndStopsAfterUnwire() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var desk = new EmiDeskService();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        ulong idleMs = 0;
        var seen = new List<(string name, object? context)>();
        desk.ClockLocal = () => now;
        desk.ClockUtc = () => now;
        desk.ReadIdleAsync = () => DesktopIdle.ReadWaylandAsync(() => Task.FromResult(idleMs));
        desk.ClockMoment = (name, context) => seen.Add((name, context));
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        desk.StartClock();
        try
        {
            foreach (var value in new ulong[] { 299999, 300000, 1199999, 1200000, 1201000, 1000, 1200000 })
            { idleMs = value; await desk.ClockTickAsync(now); }
            Assert.Equal(new[] { "ambient", "idleShort", "idleShort", "appIdleLong", "ambient", "appIdleLong" }, seen.Select(x => x.name));
            Assert.Equal(new[] { 5, 19, 20, 20 }, seen.Where(x => x.context != null)
                .Select(x => (int)x.context!.GetType().GetProperty("minutes")!.GetValue(x.context)!));
            var pending = new TaskCompletionSource<int?>();
            desk.ReadIdleAsync = () => pending.Task;
            var tick = desk.ClockTickAsync(now);
            desk.StopClock();
            seen.Clear();
            pending.SetResult(1200);
            await tick;
            Assert.Empty(seen);
        }
        finally { desk.StopClock(); }
    });

    [Fact]
    public Task WaylandPendingReadCannotFireAfterStop() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var desk = new EmiDeskService();
        var seen = new List<string>();
        var pending = new TaskCompletionSource<int?>();
        desk.ReadIdleAsync = () => pending.Task;
        desk.ClockMoment = (name, _) => seen.Add(name);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        desk.StartClock();
        try
        {
            var tick = desk.ClockTickAsync(DateTime.UtcNow);
            desk.StopClock();
            pending.SetResult(1200);
            await tick;
            Assert.Empty(seen);
        }
        finally { desk.StopClock(); }
    });

    [Fact]
    public async Task WaylandUnavailableProbeFallsBackSafely()
    {
        Assert.Null(await DesktopIdle.ReadWaylandAsync(() => Task.FromException<ulong>(new InvalidOperationException())));
        Assert.Equal(int.MaxValue, await DesktopIdle.ReadWaylandAsync(() => Task.FromResult(ulong.MaxValue)));
    }

    [Fact]
    public void WaylandIdleNotifyDrivesThresholdsAndRearming()
    {
        long t = 0;
        var notify = new WaylandIdleNotify { Now = () => t };
        var rx = new System.IO.MemoryStream(new[] { WaylandIdleNotify.Message(2, 0, 7u, "wl_seat", 7u), WaylandIdleNotify.Message(2, 0, 9u, "ext_idle_notifier_v1", 2u),
            WaylandIdleNotify.Message(3, 0, 1u), WaylandIdleNotify.Message(4, 0, 3u), WaylandIdleNotify.Message(6, 0) }.SelectMany(m => m).ToArray());
        var tx = new System.IO.MemoryStream();
        notify.Run(rx, tx);
        Assert.EndsWith(Convert.ToHexString(WaylandIdleNotify.Message(5, 2, 6u, WaylandIdleNotify.TimeoutMs, 4u)), Convert.ToHexString(tx.ToArray()));
        // Steps are seconds since start, or -1 idled and -2 resumed at the current time.
        var seen = IdleSequence(v => { if (v < 0) notify.OnEvent((uint)(-v - 1)); else t = v * 1000L; return notify.Seconds; },
            0, 899, 900, 950, -2, 1000, -1, 1900);
        Assert.Equal(new[] { "idleShort", "idleShort", "appIdleLong", "ambient", "ambient", "idleShort", "appIdleLong" }, seen);
    }

    private static void Raise(object? source, Type type, string name, params object[] args)
    {
        var field = type.GetField(name, BindingFlags.NonPublic | (source == null ? BindingFlags.Static : BindingFlags.Instance))!;
        (field.GetValue(source) as Delegate)?.DynamicInvoke(args);
    }

    [Fact]
    public void CoreEventsCarryContextAndUnwire() => AvaloniaTestDispatcher.Run(() =>
    {
        var desk = new EmiDeskService();
        var autonomy = new AutonomyScheduler();
        var speech = new SpeechEngine(null!, Array.Empty<string>());
        var daily = new DailyFreeService(fetchOverride: false);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var seen = new List<(string name, object? context)>();
        desk.ClockUtc = () => now;
        desk.ClockMoment = (name, ctx) => seen.Add((name, ctx));
        try
        {
            desk.WireAppEvents(autonomy, speech, daily);
            desk.WireAppEvents(autonomy, speech, daily);
            Raise(null, typeof(ProgressionBank), "XPAwarded", new ProgressionBank.XpAward(249, XPSource.Other, false));
            Raise(null, typeof(ProgressionBank), "XPAwarded", new ProgressionBank.XpAward(500, XPSource.Other, true));
            Raise(null, typeof(ProgressionBank), "XPAwarded", new ProgressionBank.XpAward(250, XPSource.Other, false));
            Raise(autonomy, typeof(AutonomyScheduler), "EnabledChanged", autonomy, true);
            now = now.AddMinutes(17);
            Raise(autonomy, typeof(AutonomyScheduler), "EnabledChanged", autonomy, false);
            Raise(speech, typeof(SpeechEngine), "ListeningChanged", speech, false);
            Raise(speech, typeof(SpeechEngine), "ListeningChanged", speech, true);
            Raise(daily, typeof(DailyFreeService), "TodayChanged");
            Assert.Equal(new[] { "xpBigAward", "takeoverStarted", "takeoverEnded", "sheListeningOn", "dailyFreeToday" }, seen.Select(x => x.name));
            Assert.Equal(250, seen[0].context!.GetType().GetProperty("n")!.GetValue(seen[0].context));
            Assert.Equal(EmiNames.XpSource(XPSource.Other), seen[0].context!.GetType().GetProperty("target")!.GetValue(seen[0].context));
            Assert.Equal(17, seen[2].context!.GetType().GetProperty("minutes")!.GetValue(seen[2].context));
            Assert.Equal(EmiNames.Feature(daily.TodayKey), seen[4].context!.GetType().GetProperty("target")!.GetValue(seen[4].context));
            desk.UnwireAppEvents();
            seen.Clear();
            Raise(null, typeof(ProgressionBank), "XPAwarded", new ProgressionBank.XpAward(250, XPSource.Other, false));
            Raise(autonomy, typeof(AutonomyScheduler), "EnabledChanged", autonomy, true);
            Raise(speech, typeof(SpeechEngine), "ListeningChanged", speech, true);
            Raise(daily, typeof(DailyFreeService), "TodayChanged");
            Assert.Empty(seen);
            Assert.Null(typeof(EmiDeskService).GetField("_clockTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk));
        }
        finally { desk.UnwireAppEvents(); }
    });
}
