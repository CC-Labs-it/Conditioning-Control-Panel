using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountDurationTests
{
    // Identity token only. It is never used as a native player and stays rooted for the testhost.
    private static readonly LibVLCSharp.Shared.LibVLC MismatchedOwner =
        (LibVLCSharp.Shared.LibVLC)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LibVLCSharp.Shared.LibVLC));

    // WPF BubbleCountWindow.xaml.cs:705-718. A cache hit needs no native parser.
    [Fact]
    public void CachedDurationAndNonblockingMissUseTheSharedCache()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-count-duration-");
        var cacheField = typeof(MandatoryVideoOverlay).GetField("_lengths", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = cacheField.GetValue(null);
        var ownerField = typeof(MandatoryVideoOverlay).GetField("_lengthsOwner", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousOwner = ownerField.GetValue(null);
        var cache = new VideoMetadataCache(null!);
        var gate = Field<SemaphoreSlim>(cache, "_parseGate");
        var engineGate = typeof(LibVlcAudio).GetField("_engineGate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        gate.Wait(); // Hold parsing so a miss must return without waiting for metadata.
        Monitor.Enter(engineGate); // Keep the owner stable while installing and exercising the fixture.
        try
        {
            var hit = Path.Combine(dir.FullName, "hit.mp4");
            var miss = Path.Combine(dir.FullName, "miss.mp4");
            File.WriteAllText(hit, "cached clip");
            File.WriteAllText(miss, "unknown clip");
            cache.StoreDuration(hit, 125);
            ownerField.SetValue(null, MismatchedOwner); // simulate an earlier engine before installing the fixture
            cacheField.SetValue(null, cache);
            ownerField.SetValue(null, LibVlcAudio.Shared);
            Assert.Equal(125, BubbleCountWindow.ResolveVideoDurationSeconds(hit));
            Assert.Equal(30, BubbleCountWindow.ResolveVideoDurationSeconds(miss));
            Assert.Single(Field<ConcurrentDictionary<string, byte>>(cache, "_inFlight"));
            Assert.Equal(30, BubbleCountWindow.ResolveVideoDurationSeconds(miss));
            Assert.Single(Field<ConcurrentDictionary<string, byte>>(cache, "_inFlight"));
        }
        finally
        {
            Field<Timer?>(cache, "_saveTimer")?.Dispose();
            cacheField.SetValue(null, previous);
            ownerField.SetValue(null, previousOwner);
            gate.Release();
            Monitor.Exit(engineGate);
            dir.Delete(true);
        }
    }

    // WPF BubbleCountWindow.xaml.cs:727-750. Correction changes cadence, all targets and safety;
    // repeats below one second and reports after the video ends must leave the clock alone.
    [Fact]
    public Task RealLengthRetimesActiveBubblesAndIgnoresSmallOrLateUpdates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldDuration = BubbleCountWindow.LastVideoDurationSeconds;
        var sharedTarget = typeof(BubbleCountWindow).GetField("_sharedTargetCount", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldTarget = sharedTarget.GetValue(null);
        var primary = new BubbleCountWindow("unused.mp4", BubbleCountScheduler.Difficulty.Medium, false, _ => { });
        var secondary = new BubbleCountWindow("unused.mp4", BubbleCountScheduler.Difficulty.Medium, false, _ => { }, isPrimary: false);
        var spawn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        Set(primary, "_bubbleSpawnTimer", spawn);
        void Adopt(double seconds) => typeof(BubbleCountWindow).GetMethod("AdoptRealDuration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(primary, new object[] { seconds });
        try
        {
            Adopt(120);
            Assert.Equal(120, BubbleCountWindow.LastVideoDurationSeconds);
            var target = Field<int>(primary, "_targetBubbleCount");
            Assert.Equal(target, Field<int>(secondary, "_targetBubbleCount"));
            Assert.Equal(TimeSpan.FromMilliseconds(120000.0 / target * 0.7), spawn.Interval);
            Assert.Equal(TimeSpan.FromSeconds(125), Field<DispatcherTimer>(primary, "_safetyTimer").Interval);
            Adopt(120.75);
            Assert.Equal(120, BubbleCountWindow.LastVideoDurationSeconds);
            Set(primary, "_videoEnded", true);
            Adopt(240);
            Assert.Equal(120, BubbleCountWindow.LastVideoDurationSeconds);
        }
        finally
        {
            BubbleCountWindow.ForceCloseAll();
            typeof(BubbleCountWindow).GetProperty(nameof(BubbleCountWindow.LastVideoDurationSeconds),
                BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldDuration);
            sharedTarget.SetValue(null, oldTarget);
        }
        return Task.CompletedTask;
    });

    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Set(object instance, string name, object value) => instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
