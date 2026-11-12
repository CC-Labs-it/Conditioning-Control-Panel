using System;
using System.Reflection;
using Avalonia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DeeperWebcamEventsTests
{
    [Fact]
    public void SeededAdapterForwardsAndUnsubscribesWithoutStartingCamera() => AvaloniaTestDispatcher.Run(() =>
    {
        var tracker = new WebcamTracker();
        var consent = CoreSettings.Current.WebcamConsentGiven;
        var previous = EnhancementHostService.WebcamProvider;
        var previousDispatcher = EnhancementHostService.DispatcherFactory;
        try
        {
            CoreSettings.Current.WebcamConsentGiven = false;
            EnhancementHostService.DispatcherFactory = null;
            App.SeedDeeperEnhancements(tracker);
            Assert.IsType<ConditioningControlPanel.Avalonia.Platform.Deeper.BrowserEnhancementDispatcher>(EnhancementHostService.DispatcherFactory!());
            using var adapter = Assert.IsType<DeeperWebcamEvents>(EnhancementHostService.WebcamProvider!());
            int blink = 0, mouth = 0, lost = 0, found = 0;
            (double x, double y) gaze = default;
            adapter.OnBlink += () => blink++;
            adapter.OnMouthOpen += () => mouth++;
            adapter.OnFaceLost += () => lost++;
            adapter.OnFaceFound += () => found++;
            adapter.OnGazeMove += (x, y) => gaze = (x, y);
            tracker.RaiseBlinkForTest();
            tracker.RaiseMouthForTest();
            Raise(tracker, "OnFaceLost");
            Raise(tracker, "OnFaceFound");
            Raise(tracker, "OnGazeMove", new Point(12, 34));
            Assert.Equal((1, 1, 1, 1, (12d, 34d)), (blink, mouth, lost, found, gaze));
            Assert.False(tracker.IsRunning);
            adapter.Dispose();
            tracker.RaiseBlinkForTest();
            tracker.RaiseMouthForTest();
            Raise(tracker, "OnFaceLost");
            Raise(tracker, "OnFaceFound");
            Raise(tracker, "OnGazeMove", new Point(56, 78));
            Assert.Equal((1, 1, 1, 1, (12d, 34d)), (blink, mouth, lost, found, gaze));
            Assert.False(tracker.IsRunning);
        }
        finally
        {
            CoreSettings.Current.WebcamConsentGiven = consent;
            EnhancementHostService.WebcamProvider = previous;
            EnhancementHostService.DispatcherFactory = previousDispatcher;
        }
    });

    private static void Raise(WebcamTracker tracker, string name, params object[] args) =>
        ((MulticastDelegate?)typeof(WebcamTracker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker))?.DynamicInvoke(args);
}
