using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class EngineCrashSentinelTests
{
    [Fact]
    public void Start_marks_context_and_stop_clears_even_when_already_stopped()
    {
        var s = CoreSettings.Current;
        var old = (s.FlashFrequency, s.SimultaneousImages, s.BubblesEnabled, s.MandatoryVideosEnabled, s.SubliminalEnabled, s.AudioOnlySession);
        var path = Path.Combine(CorePaths.UserData, "logs", "engine_session.active");
        try
        {
            s.FlashFrequency = 7; s.SimultaneousImages = 3;
            s.BubblesEnabled = true; s.MandatoryVideosEnabled = false; s.SubliminalEnabled = true;
            s.AudioOnlySession = true;
            CoreEngine.Start();
            var text = File.ReadAllText(path);
            Assert.Contains("flash 7/min x3 | bubbles on | video off | subliminal on", text);
            CoreEngine.Stop();
            Assert.False(File.Exists(path));
            EngineCrashSentinel.Mark("leftover");
            CoreEngine.Stop();
            Assert.False(File.Exists(path));
        }
        finally
        {
            CoreEngine.Stop();
            (s.FlashFrequency, s.SimultaneousImages, s.BubblesEnabled, s.MandatoryVideosEnabled, s.SubliminalEnabled, s.AudioOnlySession) = old;
        }
    }

    [Fact]
    public void Leftover_is_consumed_once()
    {
        EngineCrashSentinel.Mark("leftover");
        try
        {
            Assert.True(EngineCrashSentinel.ConsumeAndReport(null));
            Assert.False(EngineCrashSentinel.ConsumeAndReport(null));
        }
        finally { EngineCrashSentinel.Clear(); }
    }
}
