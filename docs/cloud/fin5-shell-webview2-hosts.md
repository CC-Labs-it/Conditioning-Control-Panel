# FIN5 shell WebView hosts

- Ported the Home browser popout: the same WebHost and native adapter move under `BeginReparenting`, preserving the document; repeated clicks focus it, closing returns it, and site navigation still finds it. WPF dimensions, minimum size, title, colors and offline gate are preserved. Panic closes all popouts, including shell-less panic routes; shell close cleans up too.
- Ported BrowserService's same-window popup routing and exact partner/ad-domain policy into Core, with sandbox, Scrolller and host navigation gates applied before navigation. No unmanaged popup surface is created.
- Matched Spiral's soft-failure boundary for navigation failures, sends and host callbacks. No extra retries.
- Replaced the duplicated historical ledger text with current evidence and an explicit remaining inventory; row remains `stub`.
- Trunk inspected read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Adaptations use WPF frozen on base `5104f1b0b9de73428ee8852e0169a69427919215`.

## WPF gap inventory on entry

Compared the cited WPF files and the BrowserService they call with existing head callers. Existing head code wins; Spiral ready/latest-state handshake, page carrier, local page server, games, intake, navigation, offline gate and page-element mute already exist. The ledger's repeated “no page navigates” and “no script channel” claims were stale.

- MainWindow.Browser / BrowserService: popout, placeholder focus and return (ported here); popup handling with partner bypass and ad rejection (ported here). Still missing: DOM-ready/title/frame-message signals; browser initialization failure/retry UI and external fallback; browser profile/cookie isolation/clear-data; engine-wide mute including cross-origin frames, native zoom; resource ad-blocking/CSS outside the popup path.
- Browser fullscreen: HTML fullscreen event, popout/embedded fullscreen ownership, original zoom/bounds/style/topmost restoration, Escape/F11 and deactivation/activation release, page forced-fullscreen flag, double-click exit, mirror teardown, avatar attachment state and session teardown.
- Browser feature callers: autoplay and BambiCloud playlists, BrowserMedia ownership/queue/takeover/stop; remote play refusal while screen-owning games are active; remote-stop homepage and blindfold restoration; haptic audio-sync injection, rearm, progress/chunk UI; browser enhancement integration. These require their corresponding media service callers, not a renamed adapter method.
- ChaosWebViewHost: complete configurable interactive/passive window lifecycle, fullscreen chrome and remembered/monitor-aware bounds; native main-window ownership/minimize/visibility/cascade handling and render kicks; per-profile browser arguments/settings; ready queue and reusable page handoff; process-failure release/counting; document-start Escape/page carrier; scoped microphone permission plus selected output sink routing; content motion preference override. Existing game/intake hosts implement subsets, not this entire contract.
- SpiralEmbedView: exceptions in navigation/ready/failure callbacks and state sends escaped; failed navigation was ignored if its URL failed the origin gate (fixed here). Still missing: dedicated browser profile/settings/arguments, native process-failure notification and direct native postMessage semantics before document scripts.
- BrowserVideoEngine: no corresponding browser-video playback engine on this head (LibVLC is the live video path). Missing mapped-media player URL/session startup, per-monitor primary/muted mirrors and floating/strict setup, volume/rate/seek/pause/resume, page events (playing/time/end/error/input/attention/credit), first-frame/grace-pause watchdog, dead-mirror handling, two-process-failure stand-down, file-blame decision and LibVLC fallback. Porting this plus its VideoService integration and the above contracts cannot fit the 1000-added-line unit cap.

## Assumptions

- Keep `stub`: this unit closes reachable host gaps but cannot claim every WPF behavior. No P122 dead-code exception applies.
- The package's documented reparenting scope supersedes the old source comment claiming NativeWebView cannot reparent. A native desktop smoke remains required.
- Popup policy applies to all shared hosts, with each host's existing allowlist retaining precedence. No host-specific duplicate policy.
- Panic closes the ordinary popout too, ensuring the new surface cannot survive teardown.

## Verification limits

- `dotnet build <project> -c Release`: exit 0, zero errors for Core, Avalonia and every test csproj named by `.github/workflows/build.yml` (15 projects total). WPF test build also succeeded with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (553 existing warnings, zero errors). Windows code was compiled but not run.
- `bash .github/scripts/core-guards.sh`: exit 0; 429 XAML references checked against 1590 app / 2207 Core types, all guards passed. The first sandboxed attempt could not create temporary files; rerun with workspace execution permissions passed.
- `.github/scripts/test-plan-coverage.sh` is absent on this base (attempt exit 127); three numbered WPF/Rows tester steps were added manually.
- Before the Spiral fix, the two regression tests failed (exit 1): the untrusted failure URL did not set HasFailed, and the navigation subscriber exception escaped. After the fix, the relevant `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter` run passed all 50 tests (exit 0): SpiralEmbedViewTests, WebHostSandboxTests, WebHostScrolllerGateTests, WebHostProbeTests, BrowserPopoutTests and PanicSurfacesTests. `DiffEngine_Disabled=true` was set for test runs.
- Two additional existing HomeMarquee browser-card tests were attempted with `--no-build --no-restore` (exit 1): both failed before their assertions because their older startup path cannot create the Segoe UI Black glyphTypeface in this environment. No tests were altered, skipped or weakened to hide this. The new popout and regression tests use the shipped app builder and pass headlessly.
- Full Core suite: first run under concurrent compilation failed only `BoardTilesTests.A_frame_fits_the_budget` (43.74 ms); 4280 passed, 5 existing skips. Isolated `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore` passed (exit 0): 4281 passed, 5 existing Vosk skips, zero failures, 42 seconds.
- `git diff --check`: exit 0. Native desktop smoke was not run: headless tests cannot prove real WebKitGTK/WebView2 reparenting, audio or rendering. No test uses a real desktop or real user profile.
