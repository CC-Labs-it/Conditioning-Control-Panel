# fin2-chaos-bubble-host

- Fix native bubble input regions: transparent corners now pass through, matching Core's circular hit test and WPF's shared-host hit discs. Preserve topmost-first model hit testing and left/right press routing.
- Rasterise discs in physical pixels with screen clipping, negative-origin and mixed-DPI conversion. Coalesce equal adjacent scanlines and reuse a growing buffer rather than silently dropping input after 64 bubbles.
- Preserve the existing removal rules when bubbles pop, become non-clickable, change screen, or disappear; test the exact region count passed to the native backend.
- Add regression coverage: exhaustive pixel comparison at 100% and 150% DPI, negative monitor origins, 70 bubbles, and removal/ineligibility. The three new cases failed against the base implementation before the fix.
- Replace the repeated historical ledger text with one accurate `stub` row. This is a partial delivery; it does not finish shared hosting.
- Read-only source branch fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; changes adapt the existing base implementation.

## WPF comparison and remaining scope

Evidence is pinned to base `a65dbbe2f4935a4797b32efd87513054dd39e1d2`:

1. `ConditioningControlPanel/Chaos/ChaosBubbleHostOverlay.cs`: one transparent Canvas window, Add/Remove, physical-pixel Place, render-scale compensation, CoversPoint fallback, primary/whole-desktop StageBounds, refcounted EnsureCreated/CloseActive (including a deferred-close recheck), RaiseActive and GetActiveHandle. Avalonia has one shared bubble drawing window per targeted screen and ambient/chaos lifetime handoff, but lacks the general four-owner host and coverage fallback API.
2. `ConditioningControlPanel/Services/BubbleService.cs`: EnsureAmbientHost/ReleaseAmbientHost, BeginChaosMode/EndChaosMode, constructor host selection, HitDiscPx/ContainsPx and PopTopmostAt. Core already uses circular, last-spawned-first hit testing; the native square region and 64-bubble cap are fixed here. Missing: the ChaosBubbleSharedHost setting still does not select between renderers. Enlarged boon/magnet hit radii are also absent from the Core bubble model; this patch matches its existing natural-size disc.
3. `ConditioningControlPanel/Services/Flash/FlashService.cs`: ShowFlashImage's useHost branch attaches a sized HostedRoot, EnsureHostRef holds the shared window, and HideFlashWindow removes it without destroying the host. Missing on Avalonia: solid-mode flash attachment, positioning, release and overlap/input ownership on the shared bubble surface. FlashOverlay still owns separate flash windows.
4. `ConditioningControlPanel/Services/Subliminal/SubliminalService.cs`: ShowSubliminalVisuals selects solid hosting only without focus stealing; ShowHostedSubliminal adds/positions/fades cards, falls back classically when CoversPoint fails, raises the host and balances its reference after stop/one-shot completion/failure. Missing on Avalonia: this hosting path and its keep-alive/fallback behavior. SubliminalOverlay still uses separate card windows.
5. `ConditioningControlPanel/Services/Chaos/ChaosModeService.cs:745-755`: RaiseGameLayerAboveVideo calls host RaiseActive. `ConditioningControlPanel/Services/Notifications/OverlayService.cs:3241` includes GetActiveHandle in attention-layer stacking repair. Missing: the Avalonia host's corresponding video raise and periodic z-order repair paths.
6. `ConditioningControlPanel/Chaos/ChaosWindowZ.cs`: BornTopmost and RaiseTopmost use PinTopmost; an unpinned run actively demotes. Missing: BubbleOverlay still uses always-topmost override-redirect windows; merely clearing Avalonia Topmost would not restore X11 window-manager control.
7. Stage targeting differs: Avalonia follows the existing global monitor target; WPF host StageBounds uses primary versus whole desktop, fixed at creation. Per-screen Avalonia rendering already compensates mixed DPI, but is not a port of WPF's one-HWND stage and CoversPoint fallback contract.

All four WPF owners have live callers at the pinned commit, as shown by `git grep -n ChaosBubbleHostOverlay a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel`. P122 does not apply. This is not a pure dependency NOOP: the native input defects were independently fixable. Remaining host/rendering/lifecycle work is substantial and is explicitly retained as missing rather than marking the row wired.

No new surface was added. Existing panic paths end ChaosRunHost/EndChaos and stop the ambient CoreBubbles owner through the engine shutdown; the existing window lifecycle is unchanged.

## Assumptions

- Keep the base head's per-target-screen windows and natural-size Core hit discs for this bounded input fix. Do not claim enlarged chaos hit targets or general shared hosting.
- Preserve the existing monitor-selection policy; report the WPF stage difference instead of changing all overlay targeting in this unit.
- Choose integer native pixel locations as the input raster convention; test the same global-DIP conversion used by the press handler.

## Verification limits

- SDK: 10.0.100. Channel-based install discovery failed with HTTP 403; the explicit 10.0.100 SDK install succeeded.
- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia and all 13 test projects referenced by `.github/workflows/build.yml`: Core, Core.Settings, Core.Content; Avalonia main, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot; and ConditioningControlPanel.Tests. Existing warnings remain.
- WPF test compilation also succeeded (exit 0, zero errors) with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Windows code was compiled, never run.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,953 passed, 5 skipped, 0 failed. Skips are the existing optional Vosk model tests; no tests were changed or disabled.
- Before fix: filter `FullyQualifiedName~BubbleOverlayTests.Native_region|FullyQualifiedName~BubbleOverlayTests.Dense_field`: exit 1, 3 failed, 0 passed. Both initial DPI cases caught square corners; the dense-field case caught the 64-bubble truncation.
- Final after-fix rerun: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~BubbleOverlayTests.Native_region|FullyQualifiedName~BubbleOverlayTests.Dense_field'`: exit 0, 5 passed, 0 skipped, 0 failed. This includes the expanded clipping and removal cases.
- Earlier full `BubbleOverlayTests` class run: exit 1, 5 passed, 1 failed. The unchanged Press_on_a_bubble_pops_it_and_pays_the_bucket test fails creating the Inter Bold glyphTypeface, matching the documented environment limitation. No test was weakened to work around it.
- An initial concurrent build/test attempt collided on Avalonia's output PDB; the subsequent sequential project builds all passed.
- `git diff --check`: exit 0.

No real X11/Windows desktop validation or native click delivery was run. Input regressions run under Avalonia Headless and inspect the exact rectangle/count pair sent to the native shim. The full Avalonia suite, WPF runtime tests and render-all were not run. Headless tests use the existing CCP_USERDATA_DIR sandbox and memory-only SecretStore. The row remains stub.
