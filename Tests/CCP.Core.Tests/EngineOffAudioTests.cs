using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EngineOffAudioTests
{
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void EngineOrSessionCountsAsRunning(bool engine, bool session, bool expected) =>
        Assert.Equal(expected, CoreEngineOffAudioLog.IsRunning(engine, session));

    [Theory]
    [InlineData("/assets/audio/clip.mp3", "clip.mp3")]
    [InlineData(@"Z:\assets\audio\clip.mp3", "clip.mp3")]
    [InlineData(null, "?")]
    [InlineData(" ", "?")]
    public void FileNamesArePrivateOnEitherOperatingSystem(string? path, string name) =>
        Assert.Equal(name, CoreEngineOffAudioLog.NameOf(path));

    [Fact]
    public void RateLimitIsPerPlayerAndResumesAtTenSecondsOrAfterClockRollback()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        Assert.NotNull(CoreEngineOffAudioLog.LineFor(a, "a.mp3", false, false, T0));
        Assert.Null(CoreEngineOffAudioLog.LineFor(a, "b.mp3", false, false, T0.AddSeconds(9)));
        Assert.NotNull(CoreEngineOffAudioLog.LineFor(b, "b.mp3", false, false, T0.AddSeconds(9)));
        Assert.NotNull(CoreEngineOffAudioLog.LineFor(a, "c.mp3", false, false, T0.AddSeconds(10)));
        Assert.NotNull(CoreEngineOffAudioLog.LineFor(a, "c.mp3", false, false, T0));
    }

    [Fact]
    public void RunningAndAskedForSoundsDoNotSpendTheDiagnosticSlot()
    {
        var player = Guid.NewGuid().ToString();
        Assert.Null(CoreEngineOffAudioLog.LineFor(player, "a.mp3", true, false, T0));
        Assert.Null(CoreEngineOffAudioLog.LineFor(player, "a.mp3", false, true, T0));
        Assert.Equal($"engine off: {player} started 'a.mp3'",
            CoreEngineOffAudioLog.LineFor(player, "/assets/a.mp3", false, false, T0));
    }

    [Fact]
    public void NoteUsesCoreSessionAndSerilog()
    {
        var engine = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var logger = Log.Logger;
        var sink = new Sink();
        using var capture = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var player = Guid.NewGuid().ToString();
        try
        {
            Log.Logger = capture;
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => true;
            CoreEngineOffAudioLog.Note(player, "/assets/clip.mp3");
            Assert.Empty(sink.Lines);
            CoreSession.IsSessionRunningProvider = () => false;
            CoreEngineOffAudioLog.Note(player, "/assets/clip.mp3", asked: true);
            Assert.Empty(sink.Lines);
            CoreEngineOffAudioLog.Note(player, "/assets/clip.mp3");
            CoreEngineOffAudioLog.Note(player, "/assets/other.mp3");
            Assert.Single(sink.Lines);
            Assert.Contains("[VideoDiag AUDIO]", sink.Lines[0]);
            Assert.Contains("clip.mp3", sink.Lines[0]);
            Assert.DoesNotContain("/assets/", sink.Lines[0]);
        }
        finally
        {
            Log.Logger = logger;
            CoreSession.IsEngineRunningProvider = engine;
            CoreSession.IsSessionRunningProvider = session;
        }
    }

    private sealed class Sink : ILogEventSink
    {
        internal readonly List<string> Lines = new();
        public void Emit(LogEvent e) => Lines.Add(e.RenderMessage());
    }
}
