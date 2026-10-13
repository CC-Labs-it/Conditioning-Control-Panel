# video-core-c: browser video gate rules and video trigger rules to CCP.Core

Base: `avalonia-port/emi-line-engine`. Branch: `avalonia-port/cloud-video-core-c`.

Already in CCP.Core for this area before this unit (left as is, WPF already routes to them):
`Services/Video/MandatoryVideoScheduler.cs` (interval, extensions, grace, attention, guard, length
filter), `Services/Video/VideoMetadataCache.cs`, `Services/Video/NoVideosReason.cs`,
`Services/BrowserFoldRule.cs`, `Services/Commands/VideoTitleMatcher.cs`, `Services/Leash/LeashVideoCap.cs`.

## What changed

- **`BrowserVideoGate` moved to Core** (`git mv` ConditioningControlPanel/Services/Video/Browser/BrowserVideoGate.cs
  -> CCP.Core/Services/Video/Browser/BrowserVideoGate.cs, shows as a rename). Same namespace and
  class name, so every WPF caller (VideoService, BubbleCountService, BubbleCountWindow) is unchanged.
  The pure half stays in the class: the Chromium container list (`IsBrowserExtension`), the
  `BrowserVideoEngineEnabled` switch and the #938 audio-device pin, now read through
  `CoreSettings.Current` and logged through Serilog.
- **Engine half behind a seam**: `BrowserVideoGate.EngineAccepts` (`Func<string, string?, bool>`).
  The WPF head seeds it in `App` startup next to `CoreSettings.ServiceProvider` with exactly the old
  checks, in the old order: `BrowserVideoEngine.Instance.IsAvailable`, `BuildPageUrl(path) != null`,
  `!BrowserUnsafeVideoCache.Contains(path, stableKey)`. Unseeded (Avalonia, tests) means no browser
  engine: every clip routes to LibVLC, as before on any failure. Behaviour on WPF is unchanged,
  including the never-throws catch.
- **`VideoTriggerRules` (new Core class, `CCP.Core/Services/Video/VideoTriggerRules.cs`)**: the first
  pure rules group out of `VideoService.cs` - `TriggerGuardDecision`, `TriggerStallCeiling` (45 s),
  `EvaluateTriggerGuard` (#1135 stale-trigger escape) and `ShouldAbandonVideoRequest` (ccp-bugs
  #1201 chaos request cancel). Removed from VideoService, which now calls the Core copy (one copy
  of each rule).
- **Tests**: `VideoTriggerGuardTests` moved (`git mv`) to Tests/CCP.Core.Tests; the four pure
  `ShouldAbandonVideoRequest` facts moved out of WPF `ChaosVideoStartWindowTests` into
  `Tests/CCP.Core.Tests/VideoRequestCancelTests.cs` (the queue-parked end-to-end facts stay in WPF and
  call the Core rule). New `Tests/CCP.Core.Tests/BrowserVideoGateTests.cs`: container list, engine
  switch, device pin, unseeded/declining/throwing engine, key pass-through, engine never consulted
  for a rejected path. WPF `BrowserSinkLabelTests` source scan of the gate now resolves through
  `SourceRoots.ReadProductFile`.
- No trunk import: `parity/trunk` has no split of these files (its BrowserVideoGate and
  VideoTriggerGuardTests are the unsplit WPF originals), so no trunk commits to credit.

## Next units (rest of this area)

1. `Video/Browser/BrowserSinkLabel.cs` (54 lines, one App.* reference) to Core.
2. `Video/Browser/BrowserUnsafeVideoCache.cs` (persistence over CorePaths + Serilog) to Core.
3. `VideoSurfaceHealth.cs` (no WPF references) to Core.
4. VideoService pure statics, second group: geometry/buffer (`DisplayAspectFrom`, `FitToAspect`,
   `ComputeBufferDims`, `StagingBytesFor`, `ComputeSnapshotDims`, `IsSnapshotFrame`, `IsRebuildCurrent`).
5. VideoService pure statics, third group: grace/timers and guards (`EvaluateGraceRequest`
   forwarders, `ShouldAutoResume`, `RemainingTimerInterval`, `ShouldSpawnTick`,
   `ShouldStartAfterPreroll`, `IsStaleGuardTick`, `EvaluateVoutMidPlay`, `ShouldAnnounceFeedDefer`).
6. Refill funnel rules from `RefillVideoQueues` (disabled-asset filter, biggest-drop text) and the
   native poison cooldown arithmetic.
7. Avalonia head: seed `BrowserVideoGate.EngineAccepts` once it has a browser video engine.

## Verification limits

- RESULTS_PLACEHOLDER
