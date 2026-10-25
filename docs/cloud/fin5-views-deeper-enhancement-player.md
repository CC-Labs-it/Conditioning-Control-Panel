# FIN5 Deeper player discovery and duration

- Base: `e2997a8468a509f44f2b0a413ebada199224b06e` (clean snapshot verified before work).
- Wire Core's existing EnhancementLibrary into the resolver on first real media discovery. The app owns/disposes the watcher; rendering alone does not create it. Embedded metadata still wins over sidecar and library. Promote embedded projects only without an existing match; preserve WPF's in-memory fallback when promotion returns no path.
- Match WPF's separate sticky no-project hint and six-second promotion hint, canceling the timer on replacement/project load/close. Audio status changes cannot erase discovery guidance.
- Publish the first positive measured local audio/video duration to the existing Core cache. Ignore stale video callbacks after switching local sources or closing; remote URLs never become local cache entries.
- Add focused headless discovery/promotion/timer tests and audio cache/sticky-hint assertions. Existing player panic registration remains sufficient: no new surface or playback backend.
- Keep the single ledger row `stub`, with explicit remaining gaps and numbered WPF comparison steps.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Implementation adapts the frozen WPF reference at the base SHA and reuses Core.

## WPF gap audit

Reference: `ConditioningControlPanel/Views/Deeper/EnhancementPlayerWindow.xaml`, `.xaml.cs`, and `.Mission3.cs` at the base SHA above. Starting gaps and disposition:

1. `BindEngineIfReady` / video binding: shared playback source, rule execution, action/diagnostic subscriptions and unbinding on unload/close are absent. Still missing: `EnhancementEngine` (1,261 reference lines) plus `EnhancementHostService` (309 lines), their time-source/action integrations and tests exceed this unit's 1,000-added-line cap. Existing audio/video read-outs are real, but are not engine clocks.
2. Browser zoom controls and Ctrl-wheel relay: still missing. The current browser host has no native zoom-factor seam; CSS scaling would not reproduce WPF.
3. HTML5/F/double-click fullscreen, page key relay, fullscreen monitor selection, dual-monitor mirror/reparent, window/topmost restoration and dark title bar: still missing. Requires browser instrumentation and platform fullscreen lifecycle, including panic teardown; retain honest stubs.
4. Library provider and embedded promotion: implemented using existing Core resolver/library. WPF order, matching check, saved-file load, in-memory fallback and source badge preserved.
5. Separate sticky no-match and six-second promotion hints: implemented; clear on new media/project and stop timer on close. Status updates remain independent.
6. Global singleton activation/owner handoff: still missing. The shell's existing per-shell reuse remains; tutorial can construct a separate player. Avalonia owner transfer needs a considered host lifecycle rather than silently destroying/recreating a playing browser.
7. Restore/save normal-window bounds with a 200x120 visible patch on the virtual desktop: still missing. No bounds persistence added in this slice.
8. Measured media-duration cache publication: implemented for local audio/video using the existing Core cache, once per file as WPF.
9. Rule-id editor focus is absent in WPF too (Mission3's log entries lack rule ids); do not invent it or claim P122 completion. Engine and browser gaps have live WPF callers, so this is not a dependency-only NOOP or a wired row.

## Verification limits

SDK: .NET 10.0.402 (channel discovery initially returned HTTP 403; a pinned 10.0.100 install also succeeded). Windows/native audio, browser, camera, monitor transitions and title-bar behavior cannot be executed in this headless Linux environment. No Windows-specific code is introduced. The test-plan coverage script is absent on this base; numbered coverage is supplied in the plan.

## Assumptions

- Prioritize the portable discovery, hint and cache gaps within the cap; do not compress the unported engine/host into a speculative abstraction or label remaining behavior wired.
- Construct the library lazily so opening a render-only player does not seed demos or start a filesystem watcher. Once real media is opened, use Core's existing library seeding/watch lifetime through app exit.

### Commands and results

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors (existing warnings).
- Release builds of every Linux test project in `.github/workflows/build.yml`: exit 0, zero errors: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `git diff --check`: exit 0. `test-plan-coverage.sh` is absent on this base.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1; 2,820 passed, five Vosk tests skipped (model unavailable), one unchanged `BoardTilesTests.A_frame_fits_the_budget` failure (54.66 ms). The timing test also failed on isolated recheck; no Core code or test was changed.
- Discovery regression was red in the combined headless run with an unset provider, though library lookup itself succeeded. Registering an unset hook when accessing the lazy library corrects that lifetime edge; explicit test providers remain supported. The pre-existing fake-provider test now restores the prior provider.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, zero errors. Final filtered `dotnet test ... --no-build --filter` covering `DeeperPlayerDiscoveryTests`, `DeeperPlayerAudioTests`, `DeeperPlayerKeysTests`, `DeeperPlayerWebcamTests`, `PanicSurfacesTests` and `CoreStandInTests.EnhancementPlayerTakesEnhancementResolverLibraryTier`: exit 0, **20 passed, zero failed/skipped**. Headless font creation worked here; the historical environment font limitation did not recur.
- Windows compilation: `dotnet build ConditioningControlPanel/ConditioningControlPanel.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. The WPF test build first failed with MSB4166 (worker node exited); retrying the same flags with `-m:1 -nr:false` passed, exit 0, zero errors. Windows tests were compiled but not run on Linux.
- Final guards passed again; all staged changes pass whitespace checks. Remaining runtime limits: native audio/video/camera, fullscreen/monitor handling and render-all were not run. No claim of full parity or `verified` status.
