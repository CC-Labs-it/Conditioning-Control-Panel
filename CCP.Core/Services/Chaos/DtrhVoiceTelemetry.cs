namespace ConditioningControlPanel.Services.Chaos;

/// <summary>WPF DtrhHostService.NoteVoicelineHeard: local, run-scoped voice totals.</summary>
public sealed class DtrhVoiceTelemetry
{
    internal long RunNumber { get; private set; }
    public bool IsRunActive { get; private set; }
    public long VoicelinesHeard { get; private set; }
    public double VoiceoverSec { get; private set; }
    public void StartRun() { RunNumber++; VoicelinesHeard = 0; VoiceoverSec = 0; IsRunActive = true; }
    public void EndRun() => IsRunActive = false;
    public void NoteVoicelineHeard(double sec)
    {
        if (!IsRunActive) return;
        VoicelinesHeard++;
        if (sec > 0 && double.IsFinite(sec)) VoiceoverSec += sec;
    }
}
