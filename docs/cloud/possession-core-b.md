# possession-core-b: move PossessionAudio to CCP.Core

Unit B of the Services/Possession Core move. Base: `avalonia-port/leash-gate-card`.

## Already in CCP.Core/Services/Possession (possession-core-a, c55d73046)
`PossessionDeck`, `PossessionOffLimits` (name rule), `PossessionContracts` (enums + bark triggers),
`EscapeKinds`, `Effects/ToastLines`, `Effects/RewritePools`.

## What changed
- `git mv ConditioningControlPanel/Services/Possession/PossessionAudio.cs -> CCP.Core/Services/Possession/PossessionAudio.cs`
  (shows as a rename under `git diff -M`). This is the ember tick and the rung/tripwire dip: the synth, the WAV writer,
  the gates (LockdownAudioTics, MasterVolume, output suppressed), the 1.5 s / 5 s throttles, and lockdown arm/disarm.
  It was the only remaining top-level Possession file with no WPF types.
- `App.*` replaced by Core seams: `CoreAudio.PlayOneShot/Duck/Unduck/DuckGeneration`, `CoreSettings.Current`,
  `CorePaths.UserData`, Serilog `Log`. One new optional seam: `CoreAudio.IsOutputSuppressedProvider`. WPF seeds it with
  `Audio == null || Audio.IsOutputSuppressed`, so the old "no audio service -> silent, no duck" gate is unchanged.
- The director stays in WPF. `PossessionAudio.Install(LockdownService, Func<bool, bool> wireDirector)` takes a callback,
  and the WPF head passes `App.WirePossessionAudio`. That callback adds or removes the now-public
  `OnEffectStarted/OnRungChanged/OnTripwireReacted` on `App.Possession`, and returns false with no director. In that case
  the cues stay disarmed until the next lockdown, as before. `ponytail:` this is a callback until the director itself
  moves to Core.
- Test seams (internal): `UtcNow`, `Settings`, `Play`, `ClipDir`, `ForgetClips`, and `Disarm` made internal.
- New `Tests/CCP.Core.Tests/PossessionAudioTests.cs` (5 facts), adapted from trunk `98cfa9d86` (CodeBambi,
  "Possession audio tics"). Trunk's panic-stop test was dropped because WPF has no panic stop for these cues (behaviour
  unchanged). Added facts: the dip ducks at strength 60; a suppressed output means silence and no duck; a lockdown wires
  and unwires the director, and retries when there was no director. There was no WPF test of PossessionAudio to move.
- Docs: `POSSESSION.md` B13 paths, `docs/avalonia-parity.md` shell-possession row.
- No Avalonia caller yet: this head has no PossessionDirector.

## Next units (Services/Possession, still in WPF)
1. **possession-core-c**: PossessionDirector scheduling rules (rung ladder, cadence, MaxLive, cooldown, scene election;
   trunk `d888c18a6`, `ffe18b811`, `021a76479` put the director in Core behind a host seam). Then `PossessionAudio.Install`
   can take the director directly and `App.WirePossessionAudio` goes.
2. **possession-core-d**: PossessionRemember (flag + next-launch charge; trunk `d959e0172`). Needs seams for
   App.Lockdown / AvatarWindow / Bark.
3. **possession-core-e**: Warden bark rules + PossessionEvents (reactive adapter, trunk `a48c71cb8`), with their WPF tests
   (`PossessionBarkRulesTests`).
4. Effect / scene catalogs (`PossessionEffectCatalog*`, `PossessionSceneCatalog`) once the effect contracts are
   head-neutral. The effects, Ghost, EmberAttribution, TransformLease and PossessionPointer are WPF visuals and stay per-head.

## Verification limits
- `dotnet build` (Linux, .NET SDK 10.0.112) succeeded for: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  succeeded. This compiles the WPF app with the new `App.WirePossessionAudio` and the IsOutputSuppressed seeding. It was
  **compiled, not run**: WPF tests and the WPF app need Windows.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1230 passed, 5 skipped, 2 failed. Both failures fail
  identically on the unmodified base (checked in a worktree of `origin/avalonia-port/leash-gate-card`) and are
  container-specific. `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` fails because the container runs
  as root, so the folder can still be deleted. `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` fails
  because the container has no IPv6 loopback.
- `PossessionAudioTests`: 5/5 pass.
- Not run: Avalonia headless tests (no Avalonia code changed), and real audio playback (the cues still play only through
  the WPF AudioService on Windows).
