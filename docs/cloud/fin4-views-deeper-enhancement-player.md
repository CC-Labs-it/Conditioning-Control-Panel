# Deeper player FIN4

- Reused the portable WebcamTracker, Core WebcamConsent and EnhancementCapabilities for WPF's nonblocking offer, dismiss/rearm, first-time guidance, explicit enable, confirmed toolbar start, stop and failure reporting.
- Playback proceeds independently. The existing player panic surface now cancels pending camera starts and confirmations. Close removes tracker subscriptions and stops owned capture.
- Added six focused headless cases and numbered manual coverage; retained `stub` with an explicit Missing list.
- Read-only trunk reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. WPF behavior adapted from pinned base `7d157ec4d6e4621234001fe7071202fbf494c98b`.

## Gap audit

References: WPF `Views/Deeper/EnhancementPlayerWindow.xaml`, `.xaml.cs`, `.Mission3.cs`. Starting ledger and head ponytail markers reviewed. Remaining gaps:

- Engine: BindEngineIfReady, BindVideoSource, EnsureVideoEngineBound, unbind/teardown, ActionLogged/Diagnostic subscriptions. No portable enhancement engine/host exists in this base. Reference engine alone is 1,261 lines and host 309, before time sources, action dispatch and behavior tests; a faithful move exceeds this unit's 1,000-added-line ceiling. Tracking now opens, but webcam-driven rules still require this engine (deeper-core-c).
- Browser: zoom clamps 0.25–5.0 in 10% increments; document-created key/double-click/Ctrl-wheel relays; HTML5/F fullscreen, restoration and emergency exit. WebHost has script execution/messages but lacks native zoom, document-created injection and fullscreen signals. CSS-only zoom would not preserve WPF behavior across navigation/fullscreen.
- Desktop: fullscreen monitor selection, dual-monitor mirror/reparent/restore, tool-window/topmost handling, dark title bar. Existing shell helpers are not a complete player fullscreen host.
- Discovery: resolver runs embedded → sidecar → library but no application LibraryMatchProvider is seeded. Embedded library promotion/deduplication, promoted badge and six-second toast are absent; missing-enhancement hints share a status field that autoplay can overwrite.
- Lifecycle: global singleton activation/minimized restore/editor-owner handoff are not matched by every launch site. Persist/restore bounds with a 200×120 visible desktop patch and once-per-media duration-cache publication are absent.
- Rule-id focus is also ignored by the WPF editor caller; this is not grounds to promote the whole row under P122.

The webcam gaps were independent and reused an existing host. This is partial implementation, not a dependency-only NOOP. No complete-parity or render-all claim.

## Assumptions

- Toolbar starts belong to the player just like banner starts and stop on close. WPF only records ownership for banner starts; owning both avoids leaving capture running after the surface closes.
- The inline offer sits above the mini-timeline using existing resources and Fluent icons, avoiding media-pane restructuring.
- Failure messages use the portable tracker error/state equivalent; arbitrary exception text is not displayed.

## Verification limits

- Required Linux builds: CCP.Core, CCP.Avalonia and all Linux test projects listed in build.yml succeeded (exit 0). Headless test project compiles with zero errors.
- Core guards passed (exit 0). Core tests: 2,821 passed, 5 existing Vosk tests skipped for absent model, exit 0. No tests weakened or disabled.
- Focused headless tests: DeeperPlayerWebcamTests, DeeperPlayerKeysTests, DeeperPlayerAudioTests, PanicSurfacesTests: 16 passed, zero failed/skipped, exit 0. Uses sandbox profile, fake audio/camera, no desktop/camera access.
- First overlapping build attempt hit an Avalonia generated-resource file lock; sequential rerun succeeded. Initial test fixture used audio with a video-only trigger, correctly rejected by validation; corrected the fixture to a valid video enhancement.
- test-plan-coverage.sh does not exist on this base; numbered coverage file added for the row.
- Native camera, native media, real key repeat, browser engine, Windows desktop/fullscreen and dual-monitor behavior were not run. WPF test cross-build with EnableWindowsTargeting and ValidateExecutableReferencesMatchSelfContained=false first failed MSB4166; retry with -m:1 succeeded (512 warnings, zero errors, exit 0, 3m38s). Compiled Windows code was not run.
