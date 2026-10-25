using System;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The settings half of WPF 7.1.5 <c>Services/Companion/CompanionService.cs</c>, same numbers: switching
    /// (:185), the perk XP modifier (:226), companion XP and its level loop (:276), the Brain Parasite drain
    /// (:393) and active time (:429). The head owns the timers and the reactions. Named apart from WPF's type.
    /// ponytail: the autonomy bonus's IsActionInProgress waits for an autonomy service on this head.
    /// </summary>
    public static class CompanionCore
    {
        public const double DrainXpPerTick = 3.0;          // WPF DRAIN_XP_PER_TICK
        public const double DrainIntervalSeconds = 2.0;    // WPF DRAIN_INTERVAL_SECONDS

        public static event Action<CompanionId, int>? LevelUp;   // WPF CompanionLevelUp, once per level
        public static event Action<CompanionId>? Switched;       // WPF CompanionSwitched

        private static DateTime _lastActiveTimeUpdate = DateTime.Now;
        private static DateTime? _lastDrainSaveUtc;

        public static CompanionId ActiveCompanion => (CompanionId)CoreSettings.Current.ActiveCompanionId;
        public static CompanionDefinition ActiveDef => CompanionDefinition.GetById(ActiveCompanion);

        public static CompanionBonusType ActivePerk => CompanionPerks.Resolve(
            CoreSettings.Current.CompanionPerk, ActiveDef.BonusType, CompanionExperience.IsV2Enabled);

        /// <summary>WPF ctor (:140): a v2 user with no saved perk gets the active bundle perk; the clock starts.</summary>
        public static void Start()
        {
            var s = CoreSettings.Current;
            if (CompanionExperience.IsV2Enabled && s.CompanionPerk == null)
            {
                s.CompanionPerk = ActiveDef.BonusType;
                CoreSettings.Save();
            }
            _lastActiveTimeUpdate = DateTime.Now;
            Log.Information("CompanionCore started. Active companion: {Companion}", ActiveDef.Name);
        }

        /// <summary>WPF SwitchCompanion (:185): free switching, active time banked to the old one.</summary>
        public static bool SwitchCompanion(CompanionId newCompanion)
        {
            var old = ActiveCompanion;
            if (old == newCompanion) return true;
            UpdateActiveTime();
            CoreSettings.Current.ActiveCompanionId = (int)newCompanion;
            CoreSettings.Save();
            var progress = CoreSettings.Current.ActiveCompanionProgress;
            if (progress.FirstActivated == DateTime.MinValue) progress.FirstActivated = DateTime.Now;
            InstalledCommunityPrompts.ApplyAssigned(newCompanion);
            try { Switched?.Invoke(newCompanion); } catch (Exception ex) { Log.Debug("CompanionSwitched handler: {E}", ex.Message); }
            Log.Information("Switched companion: {Old} -> {New}",
                CompanionDefinition.GetById(old).Name, CompanionDefinition.GetById(newCompanion).Name);
            return true;
        }

        /// <summary>WPF CalculateXPModifier (:226).</summary>
        public static double CalculateXPModifier(CompanionBonusType perk, bool autonomy, bool strict, bool noEscape, bool attentionChecks, int pinkOpacity) => perk switch
        {
            CompanionBonusType.PinkFilterBonus => pinkOpacity > 0 ? 1.0 + pinkOpacity / 100.0 : 1.0,
            CompanionBonusType.AutonomyBonus => autonomy ? 1.5 : 1.0,
            CompanionBonusType.StrictModeBonus => !strict ? 0.5 : noEscape && attentionChecks ? 2.0 : 1.0,
            _ => 1.0,
        };

        /// <summary>WPF AddCompanionXP (:276): the BASE amount, from the XP bank after its gates; the modifier
        /// reads WPF XPContext.FromCurrentSettings (:61).</summary>
        public static void AddCompanionXP(double baseAmount, string source)
        {
            var s = CoreSettings.Current;
            var id = ActiveCompanion;
            var progress = s.ActiveCompanionProgress;
            if (progress.IsMaxLevel) return;
            var modifier = CalculateXPModifier(ActivePerk, false, s.StrictLockEnabled, !s.PanicKeyEnabled,
                s.AttentionChecksEnabled, s.PinkFilterEnabled ? s.PinkFilterOpacity : 0);
            var finalAmount = baseAmount * modifier;
            Log.Debug("Companion XP: {Companion} +{Amount:F1} (base: {Base}, modifier: {Modifier:F2}x, source: {Source})",
                id, finalAmount, baseAmount, modifier, source);
            Credit(progress, finalAmount, level =>
            {
                Log.Information("Companion {Companion} leveled up to {Level}!", ActiveDef.Name, level);
                try { LevelUp?.Invoke(id, level); } catch (Exception ex) { Log.Debug("CompanionLevelUp handler: {E}", ex.Message); }
                _ = CoreHaptics.Service?.LevelUpPatternAsync();
            });
            CoreSettings.Save();
        }

        /// <summary>WPF AddCompanionXP's credit and level loop (:298-317), shared by both heads; one call per level.</summary>
        public static void Credit(CompanionProgress progress, double amount, Action<int> onLevelUp)
        {
            progress.CurrentXP += amount;
            progress.TotalXPEarned += amount;
            while (progress.CurrentXP >= progress.XPForNextLevel && !progress.IsMaxLevel)
            {
                progress.CurrentXP -= progress.XPForNextLevel;
                progress.Level++;
                onLevelUp(progress.Level);
            }
        }

        /// <summary>WPF OnDrainTick (:393): 3 player XP per tick, never below 0, the write throttled (#1311).</summary>
        public static bool DrainTick(DateTime nowUtc)
        {
            if (ActivePerk != CompanionBonusType.XPDrain) return false;
            var s = CoreSettings.Current;
            if (s.PlayerXP <= 0) return false;
            s.PlayerXP = Math.Max(0, s.PlayerXP - DrainXpPerTick);
            if (DrainSaveThrottle.ShouldSave(_lastDrainSaveUtc, nowUtc, reachedZero: s.PlayerXP <= 0))
            {
                _lastDrainSaveUtc = nowUtc;
                CoreSettings.Save();
            }
            return true;
        }

        /// <summary>WPF UpdateActiveTime (:429): the active companion's TotalActiveTime.</summary>
        public static void UpdateActiveTime()
        {
            var now = DateTime.Now;
            CoreSettings.Current.ActiveCompanionProgress.TotalActiveTime += now - _lastActiveTimeUpdate;
            _lastActiveTimeUpdate = now;
        }
    }
}
