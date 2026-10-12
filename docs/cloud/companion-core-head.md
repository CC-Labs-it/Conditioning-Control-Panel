Companion core/head (P2): the WPF 7.1.5 bark engine and companion XP in Core, the Avalonia adapters over them. Adapted from trunk `abc82671e` (`parity/trunk`). Where our base already had something, ours won.

- **Core `Services/Bark/BarkEngine`** ports WPF `BarkService.cs`: the priority walk (:1096), `_gte/_lte/_gt/_lt/_eq` conditions (:1379), the gate (:1645), the no-repeat variant rotation that is saved in AppSettings (:1793-1941), and `{0}`/`{key}` substitution (:2000). It keeps the mod-switch and avatar-click windows of WPF `BarkState`. `BarkRuleManifest` is WPF `BarkRuleLoader`: field-level mod overlay merge on Core's content ladder.
- **Core `Services/Companion/CompanionCore`** ports WPF `CompanionService.cs`: switch (:185), perk XP modifier (:226), companion XP and level loop (:276), Brain Parasite drain (:393), active time (:429). `ProgressionBank.Add` gives it the base amount after its gates (WPF `ProgressionService.AddXP:117`).
- **Pure move:** `DrainSaveThrottle` and its tests moved with `git mv` from the WPF head and WPF tests into Core and Core tests, so one copy remains.
- **Avalonia `Platform/BarkHead`** creates one engine. Its mouth is the tube. It seeds the CoreBark doorbell, the Phrase Manager bark rows, `ReloadBarkRules` and the `SettingChanged` sink (WPF App.xaml.cs:313/336-346/604), plus the brain echo (:2826). It also wires the two subscriptions the engine's own state needs: chat suppression on a user message, and the rule reload on a mod switch.
- **Avalonia `Platform/CompanionHead`** runs the drain and active-time timers and seeds the `CoreModsHooks` companion seams.
- `App.axaml.cs` starts both heads right after `StartQuests()`.
- **Next unit (line cap), marked `ponytail:` in the code:** the rest of the bark sources (flash, subliminal, bubble, video, level-ups, achievements, quests, mantra, lockdown) and the companion level-up and switch reactions (toast and sound, `best_friends`, tube lines). Also deferred: DispatchIdle and the tube idle beat, PickVoiceLine, the awareness arbiter, dry-run, the whisper and narrator gates, tier latches, condition fields that only no-source triggers read, the autonomy bonus, and community prompts.

## Verification limits

- `dotnet test Tests/CCP.Core.Tests` (all), plus `BarkEngineTests`, `CompanionCoreTests` and `DrainSaveThrottleTests` on their own. Fail-first: 18 single-rule mutations of the engine, manifest, companion core and bank each turned these tests red.
- `dotnet test Tests/CCP.Avalonia.Tests --filter CompanionBarkHeadTests`: startup order (source scan); the seams are seeded over the shipped rules; the doorbell reaches the engine.
- Built: CCP.Core, CCP.Avalonia, and the Linux test projects in build.yml. `bash .github/scripts/core-guards.sh` passes.
- Not run: the WPF head and WPF tests (Windows only). The only WPF change is the `DrainSaveThrottle` move. The real tube voice was not exercised, because headless tests have no live tube.
