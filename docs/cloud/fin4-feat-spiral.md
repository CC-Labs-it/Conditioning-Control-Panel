# fin4-feat-spiral

- Corrected clean snapshot HEAD to required base `0584d51c19463026c3ac13c25bbe6a33cf3b61e0` before implementation. Snapshot branch was `work`.
- Wired THE LOOM through existing GameWindow singleton, origin fencing and `games` panic registration. WPF ready/list, save/delete/results, reveal and stock sound frames reuse Core DtrhLoomStore. Adaptation reference: read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`, GameWindow.Loom.cs; retained this base host instead of importing trunk Dtrh dependencies.
- Token-gated store-slug-only GIF thumbnail route rejects sidecars, traversal and escaping symlinks.
- Installed WebKit string carrier before module import: bridge.js captures it once at evaluation. Existing WebView2 carrier remains intact.
- Restored live Loom-store/mod library refresh with attachment-scoped subscriptions. Reused ModArt for shipped/mod hero/side plates.
- Focused bridge/lifecycle/library/asset-boundary tests and numbered manual steps. Ledger stays **stub**.

## WPF comparison and remaining gaps

Pinned reference: base above. Read both SpiralFeatureControl files, LoomHostService, OverlayService spiral/hold paths, SessionEngine spiral/corner policy, and Controls/SpiralEmbedView. The following are live WPF behaviors; P122 does not apply:

- Muted looping UniformToFill video spiral (`OverlayService.CreateSpiralVideoWindow`, lines 1893 onward). Portable overlay only decodes images/GIFs.
- Deeper timed/sustained/ramp/pulse holds and release precedence (`OverlayService` lines 830-1234): absent overlay-effect dispatcher.
- Spiral web state: spiral:ready, latest pre-ready state queue, raw descent payload and later updates (`Controls/SpiralEmbedView.PostState/BuildState`); separate map/profile state pipeline.
- Session corner GIF: admission/dedupe, start/end, pause/resume, standalone handback and live toggle (`SessionEngine` lines 1686-2117). Portable session runner lacks this host. Full host plus the other missing pipelines and behavioral tests exceeds this unit budget; no setting-only refresh pretends to implement it.
- Session no-files delayed-start guard (`SessionEngine` lines 826-837); base omitted it because default spiral ships.
- Monitor-change random re-pick and 500 ms continued broken-pick recovery; portable head retains pick and retries once.
- Select GIF confirmation (`SpiralFeatureControl` lines 544-548).
- Loom reveal selects a file on WPF; portable launcher opens its folder. Mod audio overrides and WPF named browser_data_loom profile isolation remain absent. Loom launch usage telemetry (EmiDesk.NoteOpen) and native browser process-failure auto-close are not ported either.

User-visible Loom/library gaps prioritized; no ledger promotion, verified claim or PR.

## Verification limits

- Focused SpiralLoomTests, SpiralOverlayTests, SpiralRandomizeTests, PanicSurfacesTests and WebAssetServerTests: **31 passed, 0 failed, 0 skipped**, Release, DiffEngine_Disabled=true.
- Core guards: passed.
- `dotnet build -c Release -m:1`: Core, Avalonia and all 12 workflow test projects passed (0 errors), including WPF tests with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false. Windows binaries compiled, not executed.
- Initial full Core run: 2814 passed, 1 failed (BoardTilesTests.A_frame_fits_the_budget: 13.11 ms during concurrent WPF compilation), 5 existing Vosk tests skipped (model unavailable). Isolated follow-up recorded below.
- Regression proof: both new C# tests fail against the pinned base implementation (missing Loom window and thumbnail route). The carrier test fails against base HTML. Sources restored in finally blocks; final validation follows.
- test-plan-coverage.sh is absent on this base; added the requested numbered WPF/Rows tester steps without replacing existing coverage.
- Final readiness-fix build: 0 errors. Final focused run: 31 passed, 0 failed, 0 skipped (24 s). Stock sfx scaling and mod art repaint assertions included.
- Node carrier tests: 2 passed. Core guards and git diff --check: passed.
- Serial Core run: **2815 passed, 0 failed, 5 existing Vosk tests skipped**, 2820 total (50 s), exit 0. Board frame-budget check passed without concurrent build contention. No tests weakened, removed or newly skipped. Native web engines, real file manager, multi-monitor desktop and Windows runtime paths are not exercised headlessly. Tests use isolated CCP_USERDATA_DIR.

## Assumptions

- Reuse GameWindow and existing games panic registration instead of another web host abstraction.
- Folder reveal is an explicit parity gap.
- Keep stub until all remaining WPF paths are reachable.
