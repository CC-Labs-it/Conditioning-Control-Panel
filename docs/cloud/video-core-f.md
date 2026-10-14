# avalonia-port/cloud-video-core-f: video memory-render maths into CCP.Core

Base: `avalonia-port/quiz-core-a`.

- **New `CCP.Core/Services/Video/VideoRenderMath.cs`** (internal static, namespace
  `ConditioningControlPanel.Services`): `DisplayAspectFrom`, `FitToAspect`, `StagingBytesFor`,
  `ComputeSnapshotDims`, `IsSnapshotFrame`, `IsRebuildCurrent`, moved verbatim (bodies and
  `#687`/`#786` comments) from WPF `ConditioningControlPanel/Services/Video/VideoService.cs`.
- **WPF `VideoService.cs`**: the six methods are deleted; a file-level
  `using static ConditioningControlPanel.Services.VideoRenderMath;` keeps every call site
  (aspect fit, bitmap rebuild, frame blit, blur snapshot) unchanged, so one copy exists and behaviour is
  identical. `NeedsBlurFill` stays as the existing one-line alias of
  `MandatoryVideoScheduler.NeedsBlurFill` (already Core).
- **Tests**: `Tests/ConditioningControlPanel.Tests/BlurSurfaceFrameTests.cs` moved (git rename) to
  `Tests/CCP.Core.Tests/BlurSurfaceFrameTests.cs`; only the usings/namespace changed and the
  `NeedsBlurFill` asserts now call `MandatoryVideoScheduler.NeedsBlurFill` directly. All 24 cases kept,
  none weakened; no sleeps or delays.
- Already in Core before this unit (not duplicated): `MandatoryVideoScheduler.NeedsBlurFill`,
  `VideoSurfaceHealth`, `VideoMetadataCache`, `NoVideosReason`.
- Not touched (video-core-e unit): `ShouldStartAfterPreroll`, `IsStaleGuardTick`, `ShouldSpawnTick`,
  `ShouldAnnounceFeedDefer`, `ShouldAbandonVideoRequest`, defer ceilings.
- Trunk credit: `parity/trunk` carries no Core version of these helpers (checked with
  `git grep` on `CCP.Core`/`Tests/CCP.Core.Tests`), so nothing was imported; the source is the WPF
  reference on our base.
- WPF-only `ComputeBufferDims` (private, next to these) is left in WPF: it is not in this unit's list.

## Next units
- video-core-e (in flight in another session): preroll/stale-guard/spawn/feed-defer/abandon decisions.
- video-core-g: `ComputeBufferDims` and the memory-render watchdog decisions still private in WPF
  `VideoService` (only after video-core-e lands, same file).
- Avalonia head: use `VideoRenderMath.DisplayAspectFrom`/`FitToAspect` in
  `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs` for SAR/rotation-correct letterboxing.

## Verification limits
- Toolchain: .NET SDK 10.0.112 (Ubuntu apt package; `dotnet-install.sh` host was blocked by the egress proxy).
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, Tests/CCP.Core.Tests, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,
  Report,GeneralSettings,Snapshot}.Tests: all `Build succeeded`, 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: `Build succeeded`, 0 errors, so the WPF head
  (including the edited `VideoService.cs`) compiles. The WPF tests and the WPF app were NOT run
  (Windows only).
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests --filter FullyQualifiedName~BlurSurfaceFrameTests`: Passed 24/24.
- `dotnet test Tests/CCP.Core.Tests` (full): Passed 2802, Skipped 5, Failed 2. Both failures are
  container environment issues, in files this unit does not touch:
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the container runs as root, which
  ignores the read-only chmod the test relies on) and
  `LoopbackOAuthTests.GoodState_OnEitherLoopback_...(host: "[::1]")` (no IPv6 in the container).
- No Avalonia code changed, so no CCP.Avalonia.Tests classes were run (built only).
- Pure move: no new behaviour, so there is no new fail-first test; the 24 moved tests are the proof and
  now run on Linux CI as well.
