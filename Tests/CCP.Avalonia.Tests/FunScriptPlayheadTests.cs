using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF App.xaml.cs:3218, VideoService.cs:3410/3537/7944.
public sealed class FunScriptPlayheadTests
{
    [Fact]
    public void PlayheadReachesSubscriberUntilUnsubscribed()
    {
        long seen = -1;
        var unsubscribe = MandatoryVideoOverlay.SubscribePlaybackTime(ms => seen = ms);
        Assert.NotNull(unsubscribe);
        try
        {
            MandatoryVideoOverlay.Instance.RaisePlaybackTime(1234);
            Assert.Equal(1234, seen);
        }
        finally { unsubscribe!(); }
        MandatoryVideoOverlay.Instance.RaisePlaybackTime(5678);
        Assert.Equal(1234, seen);
    }

    [Fact]
    public Task CleanupDropsLoadedScriptAndUnsubscribesBeforePlayerTeardown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreHaptics.Service, FunScriptService.SubscribePlaybackTime);
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllText(Path.ChangeExtension(path, ".funscript"), "{\"actions\":[{\"at\":0,\"pos\":0},{\"at\":1000,\"pos\":100}]}");
        using var subscribed = new ManualResetEventSlim();
        var detached = false;
        using var haptics = new HapticService(new HapticSettings { FunScriptEnabled = true });
        try
        {
            CoreHaptics.Service = haptics;
            FunScriptService.SubscribePlaybackTime = handler =>
            {
                var off = MandatoryVideoOverlay.SubscribePlaybackTime(handler);
                subscribed.Set();
                return () => { off!(); detached = true; };
            };
            haptics.FunScript.OnVideoStarted(path);
            Assert.True(subscribed.Wait(TimeSpan.FromSeconds(10)), "companion script never subscribed");
            // Subscribe returns before Core arms its timer. Drain that startup before cleanup.
            var timer = typeof(FunScriptService).GetField("_timer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => timer.GetValue(haptics.FunScript) != null, TimeSpan.FromSeconds(10)));
            Assert.True(haptics.FunScript.IsActive);
            MandatoryVideoOverlay.Instance.CloseAll();
            Assert.False(haptics.FunScript.IsActive);
            Assert.Null(haptics.FunScript.LoadedScriptPath);
            Assert.True(detached);
        }
        finally
        {
            haptics.FunScript.OnVideoStopped();
            (CoreHaptics.Service, FunScriptService.SubscribePlaybackTime) = old;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void StartupAndNativePlayerWireTheRealClipPath()
    {
        Assert.Matches(@"FunScriptService\.SubscribePlaybackTime\s*=\s*[\w.]*MandatoryVideoOverlay\.SubscribePlaybackTime;", Src("App.axaml.cs"));
        var player = Src("Views/Overlays/MandatoryVideoOverlay.cs");
        Assert.Matches(@"_player\.TimeChanged \+=[^}]*?RaisePlaybackTime\(e\.Time\)", player);
        Assert.Matches(@"_player\.Play\(_media\);\s*try\s*\{\s*CoreHaptics\.Service\?\.FunScript.OnVideoStarted\(path\)", player);
        Assert.True(player.IndexOf("FunScript.OnVideoStopped()", StringComparison.Ordinal) < player.IndexOf("_player.Stop()", StringComparison.Ordinal));
    }

    private static string Src(string path, [CallerFilePath] string here = "") =>
        Regex.Replace(File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "../../CCP.Avalonia", path)),
            @"//[^\n]*|/\*.*?\*/", " ", RegexOptions.Singleline);
}
