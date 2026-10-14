# video-core-d: VideoService pure rules to CCP.Core

Base `avalonia-port/companion-core-b`. Moves the next group of WPF-independent rules out of
`ConditioningControlPanel/Services/Video/VideoService.cs` into `CCP.Core/Services/Video`.

## Already in Core before this unit (WPF now routed to these, no second copy left)
- `MandatoryVideoScheduler.KeepByLength`: WPF `RefillVideoQueues`' inline duration filter now calls it
  (a cache miss is still kept and still kicks the background parse).
- `PackMediaPool.ShouldDrawPack`: WPF `GetNextVideo`'s hand-rolled pack-vs-regular weighting.
- `MandatoryVideoScheduler.EvaluateGrace` / `GraceSecondsRemaining`: the WPF `EvaluateGraceRequest` /
  `GraceSecondsRemaining` wrappers are gone; callers use Core directly.
- `MandatoryVideoScheduler.KeyAction` / `GraceKey`: WPF `SetupStrictHandlers` now uses them for the strict
  Escape grace offer and the whole non-strict Escape/panic split (it already used `KeyAction` for the
  strict swallow).
- `MandatoryVideoScheduler.SkipRetrySeconds`: WPF's own `30` constant now aliases it.

## New Core rules classes
- `VideoSelectionRules` (selection): `IsRemoteMediaPath`, `WantsRemote` (Phase 4 remote blend; rolls only
  for "mixed", as WPF did), `Shuffle` (WPF `ShuffleList`), `RefillBiggestDrop` (the #1124 funnel line).
- `VideoCooldownRules` (cooldown): `NativePoisonCooldownMs` + `PoisonCooldownRemainingMs` (#766),
  `TriggerStallCeiling` / `TriggerGuardDecision` / `EvaluateTriggerGuard` (#1135),
  `MinReArmInterval` / `ShouldAutoResume` / `RemainingTimerInterval` (#735 grace timers).
- `VideoWedgeLadder` (retire/wedge ladder): `MaxRetiresPerSession`, `StallMs` / `Rung2Ms` / `Rung3Ms`,
  `NextRung` (one decision for the mandatory ladder, the pre-roll/teardown hatch-only path and the
  leased bubble-count ladder without the retire rung), `DecideRetire` (already retired / not shared /
  cap reached / retire), and `VoutMidDecision` / `EvaluateVoutMidPlay` (#600).
- Strict-lock policy: `MandatoryVideoScheduler.StrictVetoesClose` beside the existing strict key rules
  (the strict-lock home in Core), used by the WPF Closing veto (#765).

## WPF
- `VideoService.cs` keeps every side effect (logs, VideoDiag lines, native calls) and asks Core for each
  decision. Its old constants alias the Core ones, so the long comments around them still read true.
- One deliberate equivalence to note: the strict Escape grace offer used
  `string.Equals(PanicKey, "Escape", Ordinal)`; Core `PanicPolicy.EscapeIsThePanicKey` trims and ignores
  case. Panic keys are stored from `Key.ToString()`, so the two agree on every value WPF writes.

## Tests (moved as renames, then adapted to Core names)
- `Tests/ConditioningControlPanel.Tests/{VideoTriggerGuardTests,VoutMidPlayTests,VideoGracePauseTests}.cs`
  -> `Tests/CCP.Core.Tests/` (`git mv`; namespace + `using static` target, `EvaluateGraceRequest` ->
  `EvaluateGrace`).
- New `Tests/CCP.Core.Tests/VideoServiceRulesTests.cs`: remote path, remote blend (incl. no roll outside
  "mixed"), shuffle, funnel text, length filter keeps misses, poison cooldown, retire verdict, every rung of
  the three ladders, strict close veto.

## Trunk credit
- `parity/trunk` has no video-rules extraction beyond the files this base already carries
  (MandatoryVideoScheduler, NoVideosReason, VideoMetadataCache); nothing imported from it in this unit.

## Next units
- video-core-e: the remaining pure statics in `VideoService.cs`: run/pre-roll guards
  (`ShouldStartAfterPreroll`, `IsStaleGuardTick`, `ShouldSpawnTick`), `ShouldAnnounceFeedDefer`,
  `ShouldAbandonVideoRequest` (split `ChaosVideoStartWindowTests`, part of which needs the WPF
  InteractionQueue), and the defer ceilings (cascade/feed 90s).
- video-core-f: the vmem render maths (`DisplayAspectFrom`, `FitToAspect`, `StagingBytesFor`,
  `ComputeSnapshotDims`, `IsSnapshotFrame`, `IsRebuildCurrent`; WPF `NeedsBlurFill` already aliases Core)
  with `BlurSurfaceFrameTests`.
- video-core-g: remote buffer policy (low-water / cap / single-flight prefetch) as a Core class the
  Avalonia scheduler can use for `MediaSource` online/mixed.

## Verification limits
- `dotnet test Tests/CCP.Core.Tests` (Linux, .NET SDK 10.0.112): 2238 passed, 6 skipped, 2 failed. Both
  failures (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`,
  `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")`) fail identically on the untouched base
  in this container (it runs as root, so the undeletable folder deletes, and it has no IPv6 loopback);
  not touched by this unit.
- Filtered to this unit (`VideoServiceRulesTests`, `VideoGracePauseTests`, `VideoTriggerGuardTests`,
  `VoutMidPlayTests`, `MandatoryVideoScheduler*`, `PackMediaPool*`): 99/99 passed.
- `Tests/CCP.Avalonia.Tests` filtered to `MandatoryVideo|VideoTarget|VideoOverlay`: 8 passed, 1 skipped.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build -c Release` succeeded for CCP.Core, CCP.Avalonia and every Linux test csproj in
  build.yml (Core.Settings, Core.Content, Avalonia, Avalonia.Consent/Language/CatalogueStartup/Scheduler/
  Report/GeneralSettings/Snapshot).
- WPF: `Tests/ConditioningControlPanel.Tests` BUILDS on Linux with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`, so `VideoService.cs` compiles against the new
  Core rules. Neither the WPF app nor its test suite can run here (WPF/LibVLC/Win32 are Windows-only):
  the routed strict-key, wedge-ladder, retire and selection call sites are compiled, not executed.
- Added lines vs base (`git diff -M --numstat origin/avalonia-port/companion-core-b`): 576, under the 1000 cap.
