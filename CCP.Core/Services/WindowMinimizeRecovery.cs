namespace ConditioningControlPanel.Services;

/// <summary>WPF WindowChrome's attached-avatar minimize/restore custody. Saved switches stay untouched.</summary>
public sealed class WindowMinimizeRecovery
{
    private bool _resumeAutonomy, _unmuteAvatar;

    public (bool PauseAutonomy, bool MuteAvatar) Minimized(bool attached, bool autonomyRunning,
        bool lockdownActive, bool avatarMuted)
    {
        var pause = attached && autonomyRunning && !lockdownActive;
        var mute = attached && !avatarMuted;
        _resumeAutonomy |= pause;
        _unmuteAvatar |= mute;
        return (pause, mute);
    }

    public (bool ResumeAutonomy, bool UnmuteAvatar) Restored()
    {
        var result = (_resumeAutonomy, _unmuteAvatar);
        _resumeAutonomy = _unmuteAvatar = false;
        return result;
    }
}
