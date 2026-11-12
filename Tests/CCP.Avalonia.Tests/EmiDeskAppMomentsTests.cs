using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    private static List<string> IdleSequence(params int[] values)
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
        foreach (var value in values) { idle = value; desk.ClockTick(now); }
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
