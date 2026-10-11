namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>Why the remote teardown runs. Each one keeps its own rule.</summary>
    public enum RemoteEndCause
    {
        /// <summary>The remote session ended or the controller left: the subject unticked Remote
        /// Control, the server expired it, the polls failed.</summary>
        SessionEnded,
        /// <summary>The leash was cut. Its rule is unchanged: everything stops.</summary>
        LeashCut,
        /// <summary>The controller pressed panic: everything stops, whoever started it.</summary>
        Panic,
    }

    /// <summary>
    /// What the end of a remote session may stop (ccp-bugs #1405). A run the subject started for
    /// themselves (a preset, a session, the engine) is not the remote session's to end. The old
    /// teardown kept the engine flag up but stopped every service under it and cleared the
    /// overlay settings, so the run read as stopped or paused.
    /// </summary>
    public static class RemoteEndRule
    {
        /// <summary>True when the teardown must leave the engine, its services, its overlays and
        /// its queue alone and hand back only what the controller holds (remote haptics, the
        /// controller's browser video, Brain Drain it switched on, panic and strict lock).</summary>
        public static bool LeavesLocalRun(RemoteEndCause cause, bool engineRunning, bool sessionRemoteStarted)
            => cause == RemoteEndCause.SessionEnded && engineRunning && !sessionRemoteStarted;
    }
}
