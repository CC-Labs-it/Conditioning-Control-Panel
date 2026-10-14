# possession-core-d: Warden bark rules + the reactive adapter's rules into CCP.Core

Unit D of the `ConditioningControlPanel/Services/Possession` Core move.

## Already in CCP.Core for this area (audit before the move)

| Rule | Where on the base | This unit |
|---|---|---|
| `PossessionBarkTriggers` (incl. `Warden`), `PossessionRung`, `IPossessionWarden` | `CCP.Core/Services/Possession/PossessionContracts.cs` | reused, unchanged |
| `EscapeKinds` (the stare's `reason`) | `CCP.Core/Services/Possession/EscapeKinds.cs` | reused, unchanged |
| `RequestReactive` (6 s throttle, rung / intensity / photosafe / cooldown / booking / concurrency gates), `TargetFor`, `NearestTarget` | `CCP.Core/Services/Possession/PossessionDirector.cs` | reused, unchanged (the WPF adapter already routes to it) |
| Tube note loc keys `possession_tube_note_1..3` | `CCP.Core/Localization/Languages/*.json` | reused |
| Warden verbs, cooldowns, beats, knock / stare geometry, note fallback, fallback stare line | WPF `Warden.cs` only | **moved to Core** |
| Consent-setting exemption, Start / Stop match | WPF `PossessionEvents.cs` only | **moved to Core** |

## What changed

- New `CCP.Core/Services/Possession/WardenRules.cs` (pure): the four `PossessionWarden` bark verbs
  (`Verbs`), the 90 s appearance / 60 s stare cooldowns, the 15 s verb timeout, the knock beat / stare
  linger / go-home delays, `KnockShove` (12 px toward the target), `StareSpot` (centre the tube on the
  window), `NoteKeys` / `NoteFallbacks` / `NoteText` (English fallback when the language lookup misses or
  echoes the key) and `FallbackStareLine` (the line when there is no bark service).
- New `CCP.Core/Services/Possession/PossessionReactiveRules.cs` (pure): `IsConsentSetting` (the
  `LockdownPossession*` / `LockdownPhotosafe*` / `LockdownTripwires*` / `LockdownWarden*` writes the room
  never answers) and `LooksLikeStartStop(name, caption, tag)`.
- WPF `Warden.cs` and `PossessionEvents.cs` now call those rules; their private copies are deleted, so there
  is one copy of each rule. The tube glides, `PointToScreen`, the dispatcher hop, the WPF input hooks and
  `Possession.GetName` stay in the WPF head. Behaviour is unchanged (same constants, same comparisons,
  same ordinal / case rules; a null possession tag still counts as "no match").
- `git mv Tests/ConditioningControlPanel.Tests/PossessionBarkRulesTests.cs -> Tests/CCP.Core.Tests/`
  (shows as a rename). It only reads the shipped `Assets/sounds/companion_audio/mods/*/bark_rules.json`
  packs, so it now runs on Linux CI. Namespace `CCP.Core.Tests`; the repo root is found by the
  `Assets/sounds/companion_audio` folder instead of the WPF `Resources` folder; the warden-verb check reads
  `WardenRules.Verbs` so renaming a verb breaks the test instead of orphaning the pack rules.
- `Tests/CCP.Core.Tests/PossessionPureRulesTests.cs`: 6 new tests (cooldown edges, knock direction incl.
  the dead-level case, stare centring, note fallback incl. echoed key and a real translation winning,
  fallback stare lines, consent-setting exemption incl. ordinal case, Start / Stop from name, caption
  or tag).
- Trunk `a48c71cb8` (Possession reactive layer): its Core half is `RequestReactive`, which our base
  already carries (ported from the WPF director, same gates). Its extra "panic quiet" hangs off trunk's
  `PossessionDirector.PanicStop`, which neither our Core director nor the WPF reference has, so it is not
  imported here. Its head half (Avalonia `PossessionEffects` breathe, `MainShellWindow.Possession` press
  watch) is Avalonia UI and out of scope for this unit. No trunk code was copied.

## Next units

- **possession-head-reactive (Avalonia):** watch presses in the Avalonia shell and route them to
  `PossessionDirector.RequestReactive("breathe", ...)` (trunk `a48c71cb8` head half), and the setting typo via
  `PossessionReactiveRules.IsConsentSetting`; Start / Stop dodge via `LooksLikeStartStop`.
- **possession-head-warden (Avalonia):** an `IPossessionWarden` over the Avalonia avatar tube using
  `WardenRules` (verbs, cooldowns, beats, geometry, note, fallback line).
- **possession-core-e:** the remaining WPF possession files (`EmberAttribution`, `Ghost`, `TransformLease`,
  `PossessionPointer`, scenes) are WPF visuals; audit them for any pure timing / pick rules left to move.

## Verification limits

Run here (Linux container, .NET SDK 10.0.112 from the Ubuntu archive; dot.net was blocked by the proxy):

- `dotnet build` succeeded for CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Core.Settings.Tests,
  Tests/CCP.Core.Content.Tests, Tests/CCP.Avalonia.Tests and the Avalonia Consent, Language,
  CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot test projects.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  succeeded, so the WPF head (`Warden`, `PossessionEvents`) compiles against the Core rules.
  **Not run**: WPF needs Windows, so the edited WPF warden / reactive adapter were compiled, not exercised.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 2803 passed, 6 skipped, 2 failed. Both failures are environmental
  and outside this change (same two as earlier units): `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 in
  the container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (tests run as root).
  `PossessionBarkRulesTests` + `PossessionPureRulesTests`: 36/36 passed.
- Mutation check: flipping `KnockShove` to `>=` and dropping the `LockdownWarden` exemption made the new
  tests fail (2 failures), then both were restored.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Possession|Bark"`: 8/8 passed (no Avalonia code changed).
- Added lines vs `origin/avalonia-port/quiz-core-a`: 268.
