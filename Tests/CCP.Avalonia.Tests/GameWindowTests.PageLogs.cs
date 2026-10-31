using System.Collections.Generic;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebHostPageLogTests
{
    // WPF ChaosWebViewHost.cs:1700-1737: a bad frame is contained; later frames still dispatch.
    [Theory]
    [InlineData("{\"type\":{},\"level\":\"warn\",\"msg\":\"probe\"}")]
    [InlineData("{\"type\":[],\"level\":\"warn\",\"msg\":\"probe\"}")]
    [InlineData("{\"type\":\"log\",\"level\":{},\"msg\":\"probe\"}")]
    [InlineData("{\"type\":\"log\",\"level\":[],\"msg\":\"probe\"}")]
    [InlineData("{\"type\":\"log\",\"level\":\"warn\",\"msg\":{}}")]
    [InlineData("{\"type\":\"log\",\"level\":\"warn\",\"msg\":[]}")]
    public void CarrierContainsInvalidFieldsAndContinues(string malformed) => AvaloniaTestDispatcher.Run(() =>
    {
        var web = new WebHost();
        var forwarded = new List<string?>();
        web.WebMessage += body =>
        {
            var message = JObject.Parse(body);
            if (!WebHost.HandlePageLog(message, "carrier-probe")) forwarded.Add((string?)message["type"]);
        };
        var sink = new Sink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Log.Logger = logger;
            Assert.Null(Record.Exception(() => web.OnWebMessage(malformed)));
            web.OnWebMessage("{\"type\":\"log\",\"level\":\"warn\",\"msg\":\"still working\"}");
            web.OnWebMessage("{\"type\":\"ready\"}");
            var entry = Assert.Single(sink.Events, e => e.Properties.ContainsKey("Msg"));
            Assert.Equal(LogEventLevel.Warning, entry.Level);
            Assert.Equal(new ScalarValue("still working"), entry.Properties["Msg"]);
            Assert.Equal(new[] { "ready" }, forwarded);
        }
        finally { Log.Logger = previous; }
    });

    [Theory]
    [InlineData(null, LogEventLevel.Debug)]
    [InlineData("", LogEventLevel.Debug)]
    [InlineData(" ", LogEventLevel.Debug)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("unknown", LogEventLevel.Debug)]
    [InlineData(" INFO ", LogEventLevel.Information)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData(" Warn ", LogEventLevel.Warning)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("ERROR", LogEventLevel.Error)]
    [InlineData("fatal", LogEventLevel.Error)]
    public void SharedHandlerPreservesWpfLevels(string? level, LogEventLevel expected)
    {
        var sink = new Sink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Log.Logger = logger;
            Assert.False(WebHost.HandlePageLog(new JObject { ["type"] = "ready" }, "probe"));
            Assert.Empty(sink.Events);
            Assert.True(WebHost.HandlePageLog(new JObject { ["type"] = "log", ["level"] = level, ["msg"] = "page probe" }, "probe"));
            var entry = Assert.Single(sink.Events);
            Assert.Equal(expected, entry.Level);
            Assert.Equal("probe[page]: page probe", entry.RenderMessage().Replace("\"", ""));
        }
        finally { Log.Logger = previous; }
    }

    private sealed class Sink : ILogEventSink
    {
        internal readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}

public sealed partial class GameWindowTests
{
    // WPF ChaosWebViewHost.cs:1686-1732: shared host consumes logs before per-page dispatch.
    [Theory]
    [InlineData(null, LogEventLevel.Debug)]
    [InlineData("", LogEventLevel.Debug)]
    [InlineData("  ", LogEventLevel.Debug)]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("unknown", LogEventLevel.Debug)]
    [InlineData(" INFO ", LogEventLevel.Information)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData(" Warn ", LogEventLevel.Warning)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("ERROR", LogEventLevel.Error)]
    [InlineData("fatal", LogEventLevel.Error)]
    public void PageLogLevelsReachTheSinkThroughTheGameCarrier(string? level, LogEventLevel expected) => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        w.Web.OnNavigationCompleted(w.PageUrl);
        var sink = new PageLogSink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Log.Logger = logger;
            w.Web.OnWebMessage(new JObject { ["type"] = "log", ["level"] = level, ["msg"] = "page probe" }.ToString());
            var entry = Assert.Single(sink.Events);
            Assert.Equal(expected, entry.Level);
            Assert.Contains("page probe", entry.RenderMessage());
            Assert.Contains("breakoutdemo", entry.RenderMessage());
        }
        finally { Log.Logger = previous; }
    });

    [Fact]
    public void IntakeUsesTheSharedLogHandlerAndOtherFramesAreNotConsumed() => Run((_, _) =>
    {
        var sink = new PageLogSink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            Log.Logger = logger;
            var window = new IntakeHostWindow();
            window.HandleMessage("{\"type\":\"log\",\"level\":\"error\",\"msg\":\"intake probe\"}");
            var entry = Assert.Single(sink.Events);
            Assert.Equal(LogEventLevel.Error, entry.Level);
            Assert.Contains("IntakeHost", entry.RenderMessage());
            Assert.False(WebHost.HandlePageLog(new JObject { ["type"] = "ready" }, "probe"));
            Assert.Single(sink.Events);
        }
        finally { Log.Logger = previous; }
    });

    private sealed class PageLogSink : ILogEventSink
    {
        internal readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
