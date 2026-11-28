using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Arcademy;
using Xunit;

namespace CCP.Tests.Shared;

/// <summary>Saves and puts back the fake self id, the rung and the settings subscription
/// ArcademyPresenceService keeps forever, so a presence test leaks nothing into the next one.</summary>
internal static class ArcademyPresenceStatics
{
    private static readonly string[] Names = { "_self", "_lastShare", "_hookedSettings" };

    private static FieldInfo Field(string name) =>
        typeof(ArcademyPresenceService).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static object? Get(string name) => Field(name).GetValue(null);

    internal static object?[] Save() => Names.Select(Get).ToArray();

    internal static void Restore(object?[] saved)
    {
        var handler = (PropertyChangedEventHandler)Delegate.CreateDelegate(typeof(PropertyChangedEventHandler),
            typeof(ArcademyPresenceService).GetMethod("OnSettingChanged", BindingFlags.Static | BindingFlags.NonPublic)!);
        var (now, was) = (Get("_hookedSettings") as AppSettings, saved[2] as AppSettings);
        if (!ReferenceEquals(now, was))
        {
            if (now != null) now.PropertyChanged -= handler;
            if (was != null) was.PropertyChanged += handler;
        }
        for (var i = 0; i < Names.Length; i++) Field(Names[i]).SetValue(null, saved[i]);
    }

    /// <summary>A request still on the wire must reach the fake, not the restored client.</summary>
    internal static void WaitIdle()
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (ArcademyPresenceService.Pending > 0 && DateTime.UtcNow < end) Thread.Sleep(10);
        Assert.Equal(0, ArcademyPresenceService.Pending);
    }
}
