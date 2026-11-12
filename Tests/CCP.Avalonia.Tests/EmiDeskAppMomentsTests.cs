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
        desk.ClockMoment = (name, _) => seen.Add(name);
        typeof(EmiDeskService).GetProperty(nameof(EmiDeskService.IsOut))!.SetValue(desk, true);
        desk.ClockTick(now); desk.ClockTick(now);
        now = now.AddHours(2);
        desk.ClockTick(now); desk.ClockTick(now);
        Assert.Single(seen.Where(x => x == "smallHours"));
        Assert.Single(seen.Where(x => x == "morningFirst"));
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
