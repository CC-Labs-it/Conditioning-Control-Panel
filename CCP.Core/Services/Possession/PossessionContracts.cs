using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Possession;

// =====================================================================================================
//  POSSESSION - the haunted-UI layer of Lockdown. Read Services/Possession/POSSESSION.md first.
//  This file is the CONTRACT between the director (PossessionDirector), the effects (Effects/*),
//  the attribution grammar (EmberAttribution) and the host (MainWindow). Keep it small and stable.
// =====================================================================================================

/// <summary>The five rungs of the Possession Ladder, keyed to the ELAPSED FRACTION of the lockdown
/// timer (0-10% Settle, 10-35% Drift, 35-60% Melt, 60-85% Collapse, 85-100% It knows). Gentle caps at
/// Melt; Eerie (default) caps at Collapse; only Full Doki reaches It knows.</summary>
public enum PossessionRung { Settle = 0, Drift = 1, Melt = 2, Collapse = 3, ItKnows = 4 }

/// <summary>Intensity preset chosen on the Lockdown card. Stored as int in AppSettings.LockdownPossessionIntensity.</summary>
public enum PossessionIntensity { Gentle = 0, Eerie = 1, FullDoki = 2 }

/// <summary>What a registered target IS, so effects can pick fitting victims (a toggle dissolves, a
/// card falls, a title loses letters, buttons swap).</summary>
public enum PossessionRole { None = 0, Button, Card, Toggle, Title, Label, TabHeader, Timer, Slider, Combo, Image, Scroll, Progress, TextBox }


// Director surface other layers code against (implemented in PossessionDirector.cs, exposed as
// App.Possession):
//   PossessionRung CurrentRung { get; }            event Action<PossessionRung>? RungChanged;
//   bool IsHaunting { get; }                        IPossessionWarden? Warden { get; set; }
//   int LiveEffectCount { get; }                    void UndoAll();   // sync, crash-safe
//   void AttachHost(IPossessionHost host);          void DetachHost(IPossessionHost host);

/// <summary>Bark trigger names raised by the Possession layer (BarkService.NotifyPossession*).</summary>
public static class PossessionBarkTriggers
{
    public const string RungChanged = "PossessionRungChanged";   // ctx: rung (0-4)
    public const string Effect = "PossessionEffect";             // ctx: effect (id), target (display name)
    public const string Tripwire = "PossessionTripwire";         // ctx: kind, repeat, total
    public const string Warden = "PossessionWarden";             // ctx: verb (knock|stare|leave|return)
    public const string Rules = "PossessionRules";               // first-run: the warden states the rules
    public const string TimerRestarted = "PossessionTimerRestarted"; // ctx: reason, restart (count) - Emergency Exit sent them back in
    public const string Remember = "PossessionRemember";             // next launch after a Full Doki lockdown: "I remember."
    public const string Conscript = "LockdownConscript";             // ctx: features (display names), round, engine (1 = the engine was started for them) - the Dose (LockdownDoseKeeper)
}
