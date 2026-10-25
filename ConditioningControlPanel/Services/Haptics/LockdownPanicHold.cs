namespace ConditioningControlPanel.Services;

/// <summary>
/// While a Lockdown runs, nothing that writes settings wholesale (a preset, a recalled config) may
/// change the panic key. Every built-in preset carries <c>PanicKeyEnabled = true</c>, so loading one
/// mid-Lockdown switched the panic key back on under a Lockdown that had switched it off (its "no
/// panic" safety), and the load saved that to disk (owner, 2026-10-22). Lockdown owns the flag until
/// it ends and hands back the value it saved at the start (LockdownService.Activate / Deactivate).
///
/// <para>Held both ways: a preset cannot switch panic off mid-Lockdown either, since the value
/// Lockdown hands back at the end would undo it anyway. Outside a Lockdown the preset wins.</para>
/// </summary>
public static class LockdownPanicHold
{
    /// <summary>What the panic key becomes after a wholesale write.</summary>
    public static bool Keep(bool lockdownActive, bool before, bool incoming) => lockdownActive ? before : incoming;

    /// <summary>Put the panic key back after a wholesale write (Preset.ApplyTo). Pass the value read
    /// before the write.</summary>
    public static void RestoreAfterApply(Models.AppSettings s, bool panicBefore)
        => s.PanicKeyEnabled = Keep(App.Lockdown?.IsActive == true, panicBefore, s.PanicKeyEnabled);
}
