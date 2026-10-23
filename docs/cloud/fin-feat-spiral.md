# fin-feat-spiral

- Video spirals selected from the library now play silently and loop, on the existing overlay windows and VlcFrameSink. Opacity repaint leaves the decoder running; file changes and overlay teardown release it. Adapted the video slice of trunk `447dfd0b10827ae3588c31c7ef53dd20c89adc10`; no unused hold/Back Room abstractions were imported.
- THE LOOM button now opens/refocuses the standalone editor through the existing GameWindow and its registered `games` panic surface. Ready answers with the saved library; save/overwrite/delete use Core DtrhLoomStore, reveal opens the containing folder, and cues use the existing audio seam. Adapted trunk `7c193a5cb2a0b4d8a43978ef1f91837d8dd5b7f0`; the current GameWindow and card were merged into, not replaced.
- Loom's page uses the existing shared string carrier while retaining WebView2 behavior. Its authenticated thumbnail route accepts only store-whitelisted GIF names and rejects sidecars, traversal, nested names and symlinks. Live saves/deletes refresh an attached Spiral library.
- Restored mod-aware hero/side art and live default-thumbnail refresh with existing CoreMods/ModArt helpers; restored the WPF 700px collapse threshold with 48px hysteresis and filename-only Select GIF confirmation.
- Ported WPF's delayed-session missing-file guard, including its pre-start timing and session-disable behavior. The old ledger's claim that the shipped default satisfies this guard was incorrect: WPF checks an existing configured file or an application `Spirals` GIF, not its packaged fallback.
- Added focused Core, non-window/headless Avalonia, native-video and JavaScript regressions. The ledger retains exactly one `feat-spiral` row, honestly marked `stub`.

## WPF audit and remaining gaps

Reference pinned at base `0584d51c19463026c3ac13c25bbe6a33cf3b61e0`:

- Read `ConditioningControlPanel/Features/SpiralFeatureControl.xaml` and `.xaml.cs`: initial gaps were Loom launch/live store feed; mod-change thumbnail/art repaint; responsive side art; selected-file confirmation; the live session-corner-GIF policy refresh. The last one remains because the session-scoped corner overlay itself is not implemented in SessionRunner. The standalone Corner GIF slots are a separate, existing surface.
- Read `ConditioningControlPanel/Services/Notifications/OverlayService.cs`, especially CreateSpiralVideoWindow and StopSpiral: video picks had no playback surface; implemented on this head's existing frame sink. The base's randomizer deviations remain: monitor rebuilds retain the pick, and a broken random image is re-picked once rather than re-polled every 500ms.
- Read `ConditioningControlPanel/Services/Session/SessionEngine.cs:823-851` and RefreshCornerGifPolicy: the missing-file guard is now implemented; the latter still requires the complete session corner lifecycle (admission/deduplication, timing, pause/resume, panic and handback).
- Read `ConditioningControlPanel/Services/Chaos/LoomHostService.cs`: the reachable editor/store protocol is implemented. WebHost has no per-window browser-profile or process-failure seam, so those two WPF host behaviors remain absent. The portable reveal opens the containing folder rather than selecting the GIF in a native file manager.
- Deeper ramp/pulse holds still depend on the unported EnhancementHostService.Bind/rule engine/playback time source: `CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs` explicitly records that loaded rules do not fire. Importing an uncalled hold API would not make a user path work.
- Spiral ready/state bridge still depends on `ctrl-spiral-embed-view` and the descent/state driver in SpiralTabView; the feature-card editor does not substitute for the Spiral room's separate web protocol.

These are live WPF behaviors, not P122 dead code. For example, `git grep -n RefreshCornerGifPolicy 0584d51c19463026c3ac13c25bbe6a33cf3b61e0 -- ConditioningControlPanel/Features/SpiralFeatureControl.xaml.cs` finds the live checkbox caller. No `wired` or `verified` claim is made. The remaining host/system work exceeds this unit's scope and 1000-added-line budget; the reachable library/editor/playback gaps were prioritized.

## Verification limits

- SDK: .NET 10.0.402. The original cloud snapshot had a different HEAD and read-only Git metadata; used the requested fresh-clone fallback and verified the exact expected base before branching.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- All Linux test projects in `.github/workflows/build.yml` compiled with `dotnet build <project> -c Release`, 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (existing warnings). WPF/Windows code compiled but was not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (3 pre-existing baselines). The first attempt lacked temporary-file write access; rerunning in the writable execution context passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; 2817 passed, 5 existing Vosk tests skipped because the model is unavailable, 0 failed. Tests were not weakened or disabled.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --filter FullyQualifiedName~Spiral_DelayedStart`: exit 0; 3 passed. The missing-file test failed before the guard was added (expected disabled, actual enabled). The existing ramp/opacity test now supplies an existing file to satisfy WPF's guard; all previous assertions remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~Loom_thumbnails|FullyQualifiedName~Opacity_curve_matches_wpf_722|FullyQualifiedName~Frame_plan_caps_size_frames_and_scales_the_delay'`: exit 0; 9 passed. The thumbnail test failed against the pre-fix build (expected GIF path, actual null), then passed after the fix.
- `node --test Assets/web/dtrh/smoke/loom-carrier.test.mjs Assets/web/shared/tests/carrier.test.js`: exit 0; 5 passed. Re-running the Loom tests with the pinned base bridge failed its string-carrier ready test (no outgoing ready), then passed with the fix.
- Font-dependent window tests (`SpiralFinishTests` except thumbnails, existing SpiralOverlayTests/SpiralRandomizeTests) and the new native looping-video test were compiled with zero errors but not run, following the task's measured cloud-font exception. Their runtime assertions/fail-proofs require supervisor CI. LibVLC/WebKitGTK desktop playback, native file-manager launch, multi-monitor positioning, Windows behavior and WPF side-by-side renders were not run. No render-all claim is made.
- `git diff --check`: passed. Added lines against the pinned base: 718 (cap 1000). Push was attempted only for the designated branch and failed with exit 128: Git HTTPS credentials are unavailable. No authentication workaround was attempted; the full format-patch is provided in the task result. No PR was opened.

## Assumptions

- Preserve this base's random-pool and session-opacity behavior; do not import unrelated trunk effects or uncalled hold APIs.
- WPF's picker remains image-only; videos are reachable through the existing library, which already lists them.
- Reuse GameWindow's lifecycle and panic registration for Loom. The current WebHost profile/failure limitations remain explicit rather than simulated.
- Use a small local resize handler with WPF's constants instead of importing a generic attached-layout framework for one card.
- Preserve WPF's exact delayed-session file guard even though its packaged fallback is playable outside that guard.
