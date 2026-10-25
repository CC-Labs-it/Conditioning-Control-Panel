# Brain Drain Studio

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; original checkout was read-only, so the authorized fallback checkout was used.
- Added settings load/save/rebind, volume More options, mod art, merged clip discovery and folder/refresh actions.
- Added timed audio using the existing LibVLC voice implementation, live master × feature gain, non-overlap, engine lifecycle and panic registration.
- Replaced the repeated historical ledger text with one honest stub row and numbered tester steps.
- No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted directly from the pinned WPF base.

## WPF gap audit

At the pinned base the head lacked enable/start-stop, intensity updates, high-refresh restart, blur/melt/capture preference persistence, settings reload/external synchronization, volume/MoreFold, primary and legacy folder paths/counts, shell opening, refresh, mod art, and the actual audio service. Keep-clear preference was already wired. These panel controls and basic audio behaviors are implemented here. WPF audio endpoint suppression/recovery diagnostics, media-history recording and BrainDrainTriggered avatar/bark routing are not yet connected to this new player; they remain service integration gaps.

Remaining service integrations: endpoint suppression/recovery, media-history recording and BrainDrainTriggered avatar/bark routing.

Remaining visual dependency: `BrainDrainCapturePump` (771 lines), `BrainDrainLayer` (434 lines) and OverlayService integration have no Avalonia capture/compositor counterpart. Porting these faithfully, with platform capture, immutable frame publication, privacy re-arming, keep-clear ordering, device lifetime and focused tests exceeds the remaining 1000-added-line budget. Visual blur strength, melt, keep-clear ordering, capture visibility and keyword 10-second `BrainDrainMeltPayload` rendering consequently remain missing. These are live WPF behaviors, not P122 dead code. Row remains stub.

## Assumptions

- Reuse the existing LibVLC voice and shared mod-art binder rather than import NAudio/WPF dependencies.
- Show the existing localized withheld notice while desktop capture is absent.
- Audio rolls re-read the merged pool, so refresh is effective without another library cache; no user files are moved.

## Verification limits

- .NET SDK 10.0.402 installed. Builds use Release, one worker and hard-link copy options; completed auxiliary test output directories were removed between builds to fit the temporary disk quota. Initial build retries exposed a namespace collision (fixed), worker termination and disk exhaustion (resolved).
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors. Final app plus Avalonia tests rebuilt after the opener correction: exit 0, 0 errors.
- Every Linux test project named in `.github/workflows/build.yml` built successfully (exit 0, 0 errors):

  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: first run caught the new direct-shell opener (3958 passed, 1 failed, 5 existing Vosk skips). Replaced it with sandbox-aware `ExternalOpener`; repeat had 3958 passed, 1 unrelated DTRH remote-media failure (expected Loops, got Clips), 5 existing Vosk skips. That DTRH test passed in the first full run.
- Targeted Core rerun for `DtrhRemoteMediaTests.Take_ColdCacheFetches`, `BrainDrainAudioPolicyTests` and `SandboxNetTests.EveryLaunch`: exit 0, 8 passed, 0 failed. This demonstrates the launch guard failure before the fix and pass after it.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~BrainDrainPlayerTests.PlaysToEnd|FullyQualifiedName~PanicSurfacesTests'`: exit 0, 7 passed, 0 failed (also passed after the opener fix). Uses test profile sandboxes and a fake voice; no actual audio or desktop.
- Window/font-dependent Brain Drain panel tests compiled but were not run under the task's known font-creation limitation. No render-all, native playback, Windows runtime, real desktop capture or Vosk model tests run. Vosk skips are existing suite behavior; no tests were disabled or weakened.
- The base does not contain `.github/scripts/test-plan-coverage.sh`; numbered row coverage was added manually.
- `git diff --check`: exit 0.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (single worker): exit 0, 554 warnings, 0 errors. Compiled only; Windows code/tests cannot run here.
- Added-line count against the pinned base: 458 (cap 1000).
