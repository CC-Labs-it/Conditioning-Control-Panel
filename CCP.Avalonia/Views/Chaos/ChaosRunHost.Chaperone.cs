using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    internal static partial class ChaosRunHost
    {
        // WPF ChaosModeService.cs:1262: the pair replaces the ordinary spawn slot.
        private static bool TrySpawnChaperone(ChaosRunConfig cfg, double intensity)
        {
            if (_state == null || cfg.ScriptedFirstRun || !ChaosMeta.AtLeast(ChaosRank.Tempted)
                || Rng.NextDouble() >= ChaosTuning.CHAPERONE_SPAWN_CHANCE * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1)) return false;
            bool debut = !ChaosMeta.State.SeenChaperone;
            if (debut)
            {
                ChaosMeta.State.SeenChaperone = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_chaperone_title"), ChaosAnnounceKind.Item,
                    artKey: "chaperone", subText: Loc.Get("chaos_chaperone_subtitle"));
                _state.PushEvent(Loc.Get("chaos_chaperone_event"));
            }
            var (live, escort) = ChaosBubbleVariants.BuildChaperonePair(intensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1);
            ChaosMeta.MarkDiscovered("bubble:chaperone");
            BubbleOverlay.SpawnChaperone(live, escort);
            return true;
        }
    }
}
