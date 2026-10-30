using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class RemoteLocalRunTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RemoteEndPreservesOnlyALocalRun(bool panic, bool running)
    {
        var s = CoreSettings.Current;
        var audioOnly = s.AudioOnlySession;
        var flash = s.FlashEnabled;
        var sub = s.SubliminalEnabled;
        var sessions = s.TotalSessions;
        var unduck = CoreAudio.UnduckProvider;
        var unducked = false;
        try
        {
            CoreEngine.Stop();
            s.AudioOnlySession = true; // no devices or window hosts during engine startup
            if (running) CoreEngine.Start();
            s.FlashEnabled = true;
            s.SubliminalEnabled = true;
            CoreFlash.Start();
            CoreSubliminal.Start();
            Assert.True(CoreFlash.IsRunning);
            Assert.True(CoreSubliminal.IsRunning);

            CoreAudio.UnduckProvider = _ => unducked = true;
            RemoteCommands.StopEffects(panic);

            Assert.Equal(panic || !running, unducked);

            Assert.Equal(running && !panic, CoreEngine.IsRunning);
            Assert.Equal(running && !panic, CoreFlash.IsRunning);
            Assert.Equal(running && !panic, CoreSubliminal.IsRunning);
        }
        finally
        {
            CoreAudio.UnduckProvider = unduck;
            CoreEngine.Stop();
            s.AudioOnlySession = audioOnly;
            s.FlashEnabled = flash;
            s.SubliminalEnabled = sub;
            s.TotalSessions = sessions;
        }
    }
}
