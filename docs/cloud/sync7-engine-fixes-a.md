# sync7-engine-fixes-a — flash ceiling slice

- Flash bursts and direct/hydra spawns now stop at the live Max on screen slider instead of stacking up to 30. Ported WPF `6174b5c01` into `FlashOverlay`: each spawn uses Core `FlashStayRule.OnScreen` at the shared `Show` gate, before presentation or XP. Scheduled pictures read the live Max on screen slider instead of a burst-time snapshot; a full screen skips that picture and lets later pictures use room freed by a pop.
- Kept the final `4bcb62934` stay-until-popped exception: clickable ambient flashes retain their 40 ceiling; point-fired and nonclickable flashes use the slider's 1..20 ceiling. Updated the existing flash-pop assertions to the stricter ceiling.
- Added `FlashCeilingTests` for slider limits, the stay exception, refusal before presentation/payment, direct/hydra calls and live slider changes. Fixed flash test initializers to register the bundled Inter font used by the application; assertions remain intact.
- Read WPF diffs/current counterparts for `6174b5c01`, `4bcb62934`, `1feb49131`, `73438ce6e`, `734a9e477`, `175d692fe`. Fetched `parity/trunk` read-only; no trunk source imported. Existing Core logic was reused, with no duplicate pure rules or unused helpers.
- Ledger: only `6174b5c01` is ported. The feature parity row remains `stub`. Remaining rows carry follow-up lane `sync7-engine-fixes-a-next`.

## Verification limits

Commands used .NET SDK 10.0.402 with `PATH="$HOME/.dotnet:$PATH"` and `DiffEngine_Disabled=true` for tests. Builds used Release, `-m:1` and, after the first build, standard MSBuild hardlink-copy properties to fit the shared workspace disk. Test assemblies use their existing temporary `CCP_USERDATA_DIR` profiles.

- Before the implementation, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~FlashCeilingTests` observed **6 failed, 2 passed**: slider cases returned 30 and a second direct spawn was accepted.
- `bash .github/scripts/core-guards.sh`: all guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: **1 failed, 2845 passed, 5 skipped, 2851 total**. The unchanged `BoardTilesTests.A_frame_fits_the_budget` reported 40.12 ms on this concurrently building environment; the five existing Vosk tests skipped because their model is unavailable.
- Focused Core rerun (`A_frame_fits_the_budget|CoreFlashScheduleTests|FlashFxRulesTests`, `--no-build`): **1 failed, 18 passed**. Only the unchanged timing test failed again (14.21 ms). No timing assertion was changed.
- The first broad flash run encountered the existing test fixture's missing Inter collection and aborted. After registering Inter in the flash fixtures, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter "FullyQualifiedName~FlashCeilingTests|FullyQualifiedName~FlashPopTests|FullyQualifiedName~FlashWireTests|FullyQualifiedName~FlashPickTests|FullyQualifiedName~FlashDriftTests|FullyQualifiedName~FlashOverlayPlacementTests"` passed: **33 passed, 0 failed, 0 skipped**, 23 s.

- Full Core rerun after render completion (`dotnet test Tests/CCP.Core.Tests -c Release --no-build`): **2 failed, 2844 passed, 5 skipped, 2851 total**. The unchanged board timing test reported 18.98 ms, and `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` missed its timed dispatch assertion. No Core production/test source was changed in this branch.
- Explicit `dotnet build` of CCP.Core, CCP.Avalonia and all 11 Linux test projects named in `.github/workflows/build.yml`: **13 builds succeeded, zero errors**. The test projects were CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Report,Scheduler,Snapshot}.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. These are build results, not claims that the other test suites ran.

- Conditional WPF cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` restored successfully and compiled Core, but did not complete the WPF `wpftmp` phase after approximately **16 minutes**. It was stopped to avoid blocking this Linux-only slice indefinitely. **No WPF build/test pass is claimed**, and no Windows code was executed.
- An initial render attempt was stopped and restarted with explicit profile isolation after noticing that Program opens its log before processing render arguments.
- `CCP_USERDATA_DIR=/tmp/worker1-render-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/worker1-render-sandbox`: **225/225 views rendered, exit 0**. `FlashFeatureControl.png` was inspected (Max On Screen slider and stay controls); `FlashOverlayWindow.png` also rendered. The headless spawn tests prove population-limit behavior. The sandbox showcase manifest logged its expected unavailable loopback endpoint.

## Remaining work — sync7-engine-fixes-a-next

This is the largest self-contained ceiling slice. Completing the other rows would require importing currently absent runtime hosts and exceeds the unit's 1000-added-line budget:

- `4bcb62934`: there is no OCR scan/matching runtime; `KeywordTriggerHead` is a stub. Port the real keyword engine/OCR path, then match regex over joined scan tokens and union touched rectangles, with keyboard-equivalent options/timeout and tests. Preserve the stay exception already covered here.
- `1feb49131`: Avalonia lacks `EffectPayload` picture-bubble delivery. Implement its real caller, then preserve the **full configured burst plus the bubble's own picture**. Do not reintroduce the single-flash design that this commit reverted.
- `73438ce6e`: flashes currently have no clip-player path. Move pure `ClipSlotBudget` to Core with WPF calling that copy, then wire actual flash/non-flash leases, reservations, immediate/idempotent disposal, eviction and tests.
- `734a9e477`: local GIF flashes currently show their first frame; remote clip pools/materialization are absent. Add the actual animation/remote path and owned files before porting round reuse, fresh decode-cache clip paths and cap-refused upgrades.
- `175d692fe`: after those hosts exist, port cleanup outside the remote lock, fresh-file ownership in `finally`, fade eviction, bounded native teardown and `FlashClipUpgrade` behavior tests.

DtRH-only payload delivery and its unhosted GIF-cascade caller are n/a on this head; the ordinary ambient-picture/flash-clip paths above remain follow-up work.

No new overlay surface was added; existing flash Stop/panic closure remains the owner. Windows/native desktop execution was not performed. No PR, merge, queue-branch update or push outside the claimed branch was made.
