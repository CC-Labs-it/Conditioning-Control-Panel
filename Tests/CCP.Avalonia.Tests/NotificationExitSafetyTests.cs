using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class NotificationExitSafetyTests
{
    // The exit handler has many irreversible service shutdowns, so check this safety ordering
    // without running a complete application exit inside the shared test process.
    [Fact]
    public void HapticsAndTrayDisposalPrecedeNotificationCleanup()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "App.axaml.cs"));
        var start = source.IndexOf("private void OnDesktopExit(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var exit = Regex.Replace(source.Substring(start), @"//[^\n]*|/\*[\s\S]*?\*/", "");
        var guard = exit.IndexOf("Interlocked.Exchange(ref _exitHandled", StringComparison.Ordinal);
        var haptics = exit.IndexOf("CoreHaptics.Service?.ShutdownStop()", StringComparison.Ordinal);
        var tray = exit.IndexOf("?.Tray?.Dispose()", StringComparison.Ordinal);
        var notifications = exit.IndexOf("Platform.OsNotifications.Shutdown()", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < haptics && haptics < tray && tray < notifications);
        Assert.Equal(1, Regex.Matches(exit, @"Platform\.OsNotifications\.Shutdown\(\)").Count);
    }

    [Fact]
    public void RepeatedExitDoesNotTouchPendingBalloon() => AvaloniaTestDispatcher.Run(() =>
    {
        var app = (App)RuntimeHelpers.GetUninitializedObject(typeof(App));
        typeof(App).GetField("_exitHandled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, 1);
        var field = typeof(OsNotifications).GetField("_windows", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        var cleared = 0;
        using var balloon = new WindowsNotifications((IntPtr)42, 7, (_, _) => true, () => cleared++);
        try
        {
            Assert.True(balloon.Show("Title", "Body"));
            field.SetValue(null, balloon);
            typeof(App).GetMethod("OnDesktopExit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(app, new object?[] { null, null });
            Assert.Equal(0, cleared);
            Assert.Same(balloon, field.GetValue(null));
        }
        finally { field.SetValue(null, previous); }
    });

    private static string Here([CallerFilePath] string path = "") => path;
}
