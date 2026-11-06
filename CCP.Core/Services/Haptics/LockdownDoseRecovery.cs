using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Haptics;

/// <summary>WPF LockdownDoseKeeper.cs:519-575, extracted from the old fin-views-tab-lockdown
/// lane. Recovery changes saved flags only, before the head starts any feature.</summary>
public static class LockdownDoseRecovery
{
    public static void RecoverIfNeeded()
    {
        // Never consume a recovery record into CoreSettings' unsaved fallback model.
        if (CoreSettings.Service is not { } settings) return;
        Recover(Path.Combine(CorePaths.UserData, "lockdown-dose.json"), settings.Current,
            () => settings.TrySaveImmediate());
    }

    private sealed class RecoveryState
    {
        public List<string>? Flipped { get; set; }
    }

    /// <param name="save">True when the flags reached disk; SaveImmediate swallows failures, so the record is
    /// consumed only on a confirmed save (review 2026-11-06).</param>
    internal static void Recover(string path, AppSettings settings, Func<bool> save)
    {
        try
        {
            if (!File.Exists(path)) return;
            var state = JsonConvert.DeserializeObject<RecoveryState>(File.ReadAllText(path));
            if (state?.Flipped == null) return;
            ApplyFlipped(state.Flipped, settings);
            // Stricter than the old lane: keep the record if parsing or saving fails.
            if (!save())
            {
                Serilog.Log.Warning("Lockdown dose recovery: settings save failed; record kept for the next launch");
                return;
            }
            File.Delete(path);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Lockdown dose recovery failed"); }
    }

    // WPF LockdownDoseKeeper.cs at 2e5f0092e:558-567. Both heads share the borrowed-key mapping;
    // each keeps its own recovery-file and save/cleanup policy.
    internal static void ApplyFlipped(IEnumerable<string> keys, AppSettings settings)
    {
        foreach (var key in keys)
        {
            switch (key)
            {
                case "flash": settings.FlashEnabled = false; break;
                case "subliminal": settings.SubliminalEnabled = false; break;
                case "spiral": settings.SpiralEnabled = false; break;
                case "pinkfilter": settings.PinkFilterEnabled = false; break;
                case "bouncingtext": settings.BouncingTextEnabled = false; break;
                case "bubbles": settings.BubblesEnabled = false; break;
                case "video": settings.MandatoryVideosEnabled = false; break;
            }
        }
    }
}
