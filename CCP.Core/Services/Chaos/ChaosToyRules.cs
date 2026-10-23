using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The rules half of the hero toy buttons (WPF ChaosToyButtonWindow + ChaosModeService): which
/// toys a run gets and on which key (BuildActiveToys :2491), which key fires which toy, with the
/// panic key outranking every toy (OnToyKey :2652), the gate + cooldown/charge half of UseToy
/// (:2684) and the cooldown countdown of TickActiveToys (:3010). The toy EFFECTS (vibe sweep,
/// freeze, DVD, snap, rabbits, e-stim) live with the bubble field and are not here.
/// </summary>
public static class ChaosToyRules
{
    public enum UseResult { Ignored, Denied, Fired }

    /// <summary>WPF BuildActiveToys: slot order = catalogue order, one toy per sewn pocket,
    /// slot 1 on <paramref name="key1"/>, slot 2 on <paramref name="key2"/>; charge-based toys
    /// (no use cooldown) start with their power as charges.</summary>
    public static void Build(ChaosRunState s, int pockets, Func<string, bool> isActive, string? key1, string? key2)
    {
        s.ActiveToys.Clear();   // zero pockets = no toy buttons, even with a stale active flag (slot >= pockets)
        string[] keys = { key1 ?? "Q", key2 ?? "E" };
        int slot = 0;
        foreach (var b in ChaosLifetimeBoons.All)
        {
            if (slot >= pockets) break;
            if (!b.IsActiveUse || !isActive(b.Id)) continue;
            if (!s.ToyPower.TryGetValue(b.Id, out var power)) continue;
            var toy = new ChaosToyState
            {
                Id = b.Id, Name = b.Name, Glyph = b.Glyph, Desc = b.Desc, Flavor = b.Flavor, CapstoneDesc = b.CapstoneDesc,
                KeyLabel = slot < keys.Length ? keys[slot] : "",
                CooldownSec = b.UseCooldownSec,
            };
            if (b.UseCooldownSec <= 0) toy.ChargesLeft = (int)power;
            s.ActiveToys.Add(toy);
            slot++;
        }
    }

    /// <summary>A stored key name as the toy keybind reads it: the digit row ("D1") is "1".</summary>
    public static string KeyName(string key) =>
        key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) ? key[1..] : key;

    /// <summary>WPF OnToyKey: the toy bound to <paramref name="key"/>, or null. The panic key
    /// outranks everything: while panic is enabled its key never fires a toy.</summary>
    public static ChaosToyState? Match(IEnumerable<ChaosToyState> toys, string key, bool panicEnabled, string? panicKey)
    {
        var name = KeyName(key);
        if (panicEnabled && !string.IsNullOrEmpty(panicKey)
            && KeyName(panicKey).Equals(name, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var toy in toys)
            if (!string.IsNullOrEmpty(toy.KeyLabel) && KeyName(toy.KeyLabel).Equals(name, StringComparison.OrdinalIgnoreCase))
                return toy;
        return null;
    }

    /// <summary>WPF UseToy's gate and cooldown bookkeeping. <paramref name="running"/> is WPF's
    /// "_spawning and not paused". Denied = WPF's toy_denied buzz.</summary>
    public static UseResult Use(ChaosRunState s, ChaosToyState toy, bool running)
    {
        if (!running) return UseResult.Ignored;
        if (s.ActivesDisabled)
        {
            s.PushEvent("🫦 the urge holds your hands — no toys");   // the urge sin
            return UseResult.Denied;
        }
        if (!toy.IsReady) return UseResult.Denied;
        double power = s.ToyPower.TryGetValue(toy.Id, out var p) ? p : 0;
        switch (toy.Id)
        {
            case "freeze_trigger":
                if (toy.ChargesLeft <= 0) return UseResult.Denied;
                toy.ChargesLeft--;
                toy.CooldownRemainingSec = 3;   // anti-doubletap between charges
                break;
            case "snap_field":
                toy.CooldownRemainingSec = Math.Max(5, power);   // level value IS the cooldown
                break;
            case "vibe_popping": case "porn_dvd": case "rabbit_caller": case "e_stim":
                toy.CooldownRemainingSec = toy.CooldownSec;
                break;
            default:
                return UseResult.Ignored;
        }
        toy.IsEffectActive = true;
        return UseResult.Fired;
    }

    /// <summary>WPF TickActiveToys' toy loop: count cooldowns down; returns the toys whose cooldown
    /// just lapsed with a charge left (the ready ding). Spent charge toys stay quiet.</summary>
    public static List<ChaosToyState> Tick(IEnumerable<ChaosToyState> toys, double dt)
    {
        var ready = new List<ChaosToyState>();
        foreach (var t in toys)
        {
            if (t.CooldownRemainingSec <= 0) continue;
            t.CooldownRemainingSec -= dt;
            if (t.CooldownRemainingSec <= 0 && t.ChargesLeft != 0) ready.Add(t);
        }
        return ready;
    }
}
