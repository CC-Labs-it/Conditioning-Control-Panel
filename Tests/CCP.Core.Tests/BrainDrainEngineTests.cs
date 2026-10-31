using ConditioningControlPanel;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public class BrainDrainEngineTests
{
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    public void EngineArmsByFlagsReconcilesAndAlwaysStops(bool enabled, bool audioOnly, int expected)
    {
        int starts = 0, stops = 0;
        var oldStart = CoreEngine.StartBrainDrain; var oldStop = CoreEngine.StopBrainDrain;
        var s = CoreSettings.Current; var oldEnabled = s.BrainDrainEnabled; var oldAudio = s.AudioOnlySession;
        try
        {
            CoreEngine.StartBrainDrain = () => starts++;
            CoreEngine.StopBrainDrain = () => stops++;
            s.BrainDrainEnabled = enabled; s.AudioOnlySession = audioOnly;
            CoreEngine.ApplyLive("braindrain", true); Assert.Equal(0, starts);
            CoreEngine.Start(); Assert.Equal(expected, starts);
            CoreEngine.ApplyLive("braindrain", true); Assert.Equal(expected + 1, starts);
            CoreEngine.ApplyLive("braindrain", false); Assert.Equal(1, stops);
            s.BrainDrainEnabled = false; CoreEngine.Reconcile(); Assert.Equal(2, stops);
            CoreEngine.Stop(); Assert.Equal(3, stops);
            CoreEngine.Stop(); Assert.Equal(4, stops);
            CoreEngine.StopBrainDrain = () => throw new System.InvalidOperationException("decoder stop");
            CoreEngine.Stop(); Assert.False(CoreEngine.IsRunning);
        }
        finally
        {
            CoreEngine.StartBrainDrain = oldStart; CoreEngine.StopBrainDrain = oldStop;
            s.BrainDrainEnabled = oldEnabled; s.AudioOnlySession = oldAudio;
        }
    }
    [Fact]
    public void SessionsStopGlobalBrainDrainOnStartAndPause()
    {
        var oldStart = CoreEngine.StartBrainDrain; var oldStop = CoreEngine.StopBrainDrain; var oldSession = CoreSession.IsSessionRunningProvider;
        var s = CoreSettings.Current; var enabled = s.BrainDrainEnabled; var audioOnly = s.AudioOnlySession;
        var runner = new ConditioningControlPanel.Services.SessionRunner(new ConditioningControlPanel.Services.SessionLogService());
        int starts = 0, stops = 0;
        CoreEngine.Stop();
        try
        {
            CoreEngine.StartBrainDrain = () => starts++; CoreEngine.StopBrainDrain = () => stops++;
            s.BrainDrainEnabled = true; s.AudioOnlySession = false;
            runner.Start(new ConditioningControlPanel.Models.Session()); Assert.Equal((1, 1), (starts, stops));
            runner.Pause(); Assert.Equal(2, stops);
        }
        finally
        {
            runner.Stop(); CoreEngine.Stop(); CoreEngine.StartBrainDrain = oldStart; CoreEngine.StopBrainDrain = oldStop;
            s.BrainDrainEnabled = enabled; s.AudioOnlySession = audioOnly; CoreSession.IsSessionRunningProvider = oldSession;
        }
    }
}
