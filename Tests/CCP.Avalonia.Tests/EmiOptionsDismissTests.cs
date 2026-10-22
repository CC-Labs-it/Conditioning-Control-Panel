using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiOptionsDismissTests
{
    private sealed class Listener : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task EventClosesWithoutAPollAndPanelClickDoesNotFold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var logs = new LogCapture();
        Setup();
        Action<PixelPoint?, bool>? press = null;
        var listener = new Listener();
        using var window = new WindowCleanup(new EmiOptionsWindow
        {
            StartDismissListener = callback => { press = callback; return listener; },
            ReadPointer = () => throw new InvalidOperationException("Event backend must not poll"),
        });
        var win = window.Window;
        win.OpenPanel();
        Dispatcher.UIThread.RunJobs();
        Assert.True(press != null, logs.Failure ?? "Dismiss listener was not installed");
        press!(win.Position + new PixelPoint(5, 5), false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(win.IsOpen);
        // Even a press released before 30 ms is an event; no poll is driven here.
        press(new PixelPoint(-5000, -5000), false);
        Assert.True(win.IsOpen); // native callback only posts
        Dispatcher.UIThread.RunJobs();
        Assert.False(win.IsOpen);
        Assert.Equal(1, listener.Disposals);
        win.ClosePanel();
        Assert.Equal(1, listener.Disposals);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OldQueuedEscapeCannotCloseAReopenedPanelAndKillUnsubscribes() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Action<PixelPoint?, bool>? press = null;
        var first = new Listener();
        var second = new Listener();
        int starts = 0;
        using var window = new WindowCleanup(new EmiOptionsWindow
        {
            StartDismissListener = callback => { press = callback; return starts++ == 0 ? first : second; },
        });
        var win = window.Window;
        win.OpenPanel();
        press!(null, true);
        win.ClosePanel();
        win.OpenPanel();
        Dispatcher.UIThread.RunJobs();
        Assert.True(win.IsOpen);
        Assert.Equal(1, first.Disposals);
        press!(null, true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(win.IsOpen);
        Assert.Equal(1, second.Disposals);
        win.OpenPanel();
        win.Kill();
        Assert.False(win.IsOpen);
        Assert.Equal(2, second.Disposals);
        win.OpenPanel();
        Assert.False(win.IsOpen);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DirectWindowCloseAlsoDisposesTheListener() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var listener = new Listener();
        var win = new EmiOptionsWindow { StartDismissListener = _ => listener };
        win.OpenPanel();
        win.Close();
        Assert.False(win.IsOpen);
        Assert.Equal(1, listener.Disposals);
        return Task.CompletedTask;
    });

    /// <summary>X11 backend rules (WPF LeftDown/RightDown + Escape; Wayland focus drop folds).</summary>
    [Fact]
    public void X11ListenerFoldsOnLeftRightAndWaylandFocusOnly()
    {
        var x11 = typeof(EmiOptionsWindow).Assembly
            .GetType("ConditioningControlPanel.Avalonia.Platform.X11OptionsDismiss")!;
        bool Button(int b) => (bool)x11.GetMethod("IsDismissButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { b })!;
        bool Left(long from, long to) => (bool)x11.GetMethod("FocusLeftX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { (IntPtr)from, (IntPtr)to })!;
        Assert.True(Button(1));
        Assert.True(Button(3));
        Assert.False(Button(2));   // middle
        Assert.False(Button(4));   // wheel
        Assert.True(Left(0x400001, 0));       // a Wayland-native window took focus
        Assert.False(Left(0x400001, 0x600002)); // another X window: its raw click already decided
        Assert.False(Left(0, 0));             // repeated None is not a new click
        var mask = (int)x11.GetField("RawMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal((1 << 13) | (1 << 15), mask); // XI_RawKeyPress | XI_RawButtonPress, nothing that could consume
    }

    private sealed class LogCapture : Serilog.Core.ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger _previous = Serilog.Log.Logger;
        public string? Failure;
        public LogCapture() => Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        public void Emit(Serilog.Events.LogEvent entry)
        {
            if (entry.Exception != null && entry.RenderMessage().Contains("options"))
                Failure = entry.Exception.ToString();
        }
        public void Dispose() => Serilog.Log.Logger = _previous;
    }

    private sealed class WindowCleanup(EmiOptionsWindow window) : IDisposable
    {
        public EmiOptionsWindow Window { get; } = window;
        public void Dispose() => Window.Kill();
    }
}
