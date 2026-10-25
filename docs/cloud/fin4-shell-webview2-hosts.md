# Shell web hosts follow-up

- Base and WPF reference: `5cdc31ee4412d4ff8a47de9dbfc42cbc39440107`.
- Restore a hidden/minimized panel before focusing a browser link, following WPF `FocusBrowserSurface` / `RestoreWindowForBrowserSurface`.
- If the adapter is unavailable, retain the URL in the fallback panel and try the system browser for HTTPS only, after the offline gate. Reuse `ExternalOpener` and its sandbox guard; preserve the navigation acceptance return value even if external launch fails.
- Complete mod-driven radio synchronization using the existing ModService. The default URL determines selection independently of the visibility override; preserve a known site's selection when the mod offers BambiCloud; navigation after a changed selection requires an explicit request and online mode.
- Prevent the companion launcher from externally opening a link after the shell rejects it for offline mode.
- Add focused headless regression cases and numbered WPF tester steps. No new CCP surface: existing shell/game/intake panic registrations suffice.
- Replace repeated historical ledger text with current capabilities and an explicit Missing list. Keep `shell-webview2-hosts` **stub**.
- Trunk fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk implementation imported. Changes adapt the pinned WPF reference and reuse the current head.

## WPF gap inventory

Compared the cited references with WebHost, the browser card/shell partial, SpiralEmbedView, GameWindow and feature hosts. Historical claims that no page uses WebAssetServer or that WebHost has no script channel are obsolete. Intake, games and other feature hosts already use the token-gated server, scripts and JSON carrier. Unrelated profile methods colocated in MainWindow.Browser.cs belong to their own ledger rows.

### MainWindow/MainWindow.Browser.cs

Present: site navigation, offline toast, live URL/catalogue lookup, script-based mute. This unit adds tray/minimized restoration, missing-adapter HTTPS escape and WPF mod-radio synchronization.

Remaining:

- Initialization generation/ready queue, pending navigation and stopped-during-init cancellation; ready timeout, runtime/init failure recovery and external escape when an existing engine fails to become ready. Native engine mute across cross-origin frames.
- Autoplay after DOM/navigation readiness and the 1500 ms render delay; video polling (20 x 500 ms), metadata/cut-start, blocked-play retry, ended/double-click/fullscreen exit. BambiCloud hydration polling (20 x 250 ms), audio binding and 3 s started fallback.
- Always-on browser media reporter and session ownership/watchdog; haptic audio-sync detection/state/seek/rearm; remote play/stop and late-fullscreen refusal.
- Same-live-page pop-out (1024 x 768, minimum 400 x 300), focus existing window, placeholder/button state, detach before close and restore afterward. NativeWebView reparenting cannot be assumed to preserve a live adapter/page.
- Native fullscreen handoff, zoom restoration, monitor mirroring, borrowed topmost on activation/deactivation, window-level Esc/F11 and teardown restoration.

### Chaos/ChaosWebViewHost.cs

Present in per-feature GameWindow/Intake/other hosts: ready queues, JSON messaging, navigation gates, window controls, panic closures; WebAssetServer replaces virtual hosts.

Remaining shared-host behavior:

- Passive/input-enabled policies; all placement/remembered bounds, main-window ownership/cascade-hide veto and render visibility repair; game-versus-any-host counts; complete page-driven versus host-owned fullscreen/escape/focus policies.
- Engine settings for context menus, accelerators, zoom, error pages and devtools; profile/environment arguments and reduced motion at engine creation.
- Document-created content-ready and escape scripts; local-page audio-sink routing and permissions; per-host core-created timing; process-failure notification and native-failure disposal/count release. Feature-specific implementations do not establish every shared-host policy.

### Controls/SpiralEmbedView.cs

Present: URL modes/origin gate, opaque background, ready/state handshake, latest-state queue, navigation success/failure splash handoff and disposal.

Remaining: native process-failure notification and suppression of native browser chrome/error pages; exception isolation around consumer navigation callbacks. WPF deliberately has no decorative retry loop.

### Services/Video/Browser/BrowserVideoEngine.cs

Core browser gate/cache/sink-label rules and the classic video path exist. Missing BrowserVideoEngine, BrowserVideoWindow, BrowserVideoSurface and VideoService integration:

- Safe virtual mappings for install/assets/packs/temp files; shared environment warm-up and two-process-failure session circuit breaker.
- Primary/secondary/floating surfaces, sink selection, strict state, blur/rate/fade/cursor/credit payloads; ready/load/player protocol, volume/pause/resume/seek and credit outro.
- 20 s pre-ready and 10 s no-playing watchdog, 500 ms ticks and paused-host exclusion; secondary drop versus strict opaque retention, file-blame classification and idempotent classic fallback.
- Stop/dispose/generation cancellation and consumer events.

These are live WPF paths: P122 does not apply. Remaining work is not exclusively a dependency, so this unit makes independent user-visible navigation fixes first. A faithful mandatory-browser-video host alone spans the 1044-line engine plus windows/surfaces and VideoService integration. That host plus shared Chaos/browser-media lifecycle cannot responsibly fit this unit's 1000-added-line cap. Do not promote the row until real callers reach these behaviors.

## Assumptions

- A known missing adapter is an immediate initialization failure: external HTTPS escape runs immediately instead of waiting for the WPF ready timeout. Existing-engine crashes/timeouts remain missing.
- Follow WPF's substring-based known-site/default selection rules for the smallest behavioral diff.
- System browser launch is an external application action, not a new CCP panic surface. Tests retain the sandbox guard.

## Verification limits

Native WebView2, WebKit rendering, OS browser launch, tray integration, fullscreen and multi-monitor behavior require desktop validation. Windows compilation does not establish Windows runtime behavior. Build/test results follow below.

- .NET SDK 10.0.402. All 14 Linux projects selected from `.github/workflows/build.yml` compiled successfully in Release (exit 0, zero errors). Native libraries initially exhausted temporary storage; generated outputs were removed and remaining builds rerun successfully with shared temporary output on the larger volume.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 553 warnings, zero errors. WPF/Windows runtime tests were not run on Linux.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4272 passed, 0 failed, 5 existing Vosk-dependent skips (4277 total). `DiffEngine_Disabled=true` for test commands.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on the pinned base (exit 127); numbered WPF tester steps were still added.
- Regression proof: `--filter FullyQualifiedName~CompanionLinkLauncherTests.OfflineRejection` failed before the guard (exit 1; expected 0 external opens, actual 1). After the fix, `--filter FullyQualifiedName~CompanionLinkLauncherTests` passed all 6 cases (exit 0).
- Initial window-regression attempts with a bare AppBuilder failed during shell construction on Segoe UI Black glyph creation, before assertions. The tests now use the production builder and its bundled-font configuration. No product or test assertion was weakened for this setup correction.
- With production fonts, both original window tests failed at behavior assertions (exit 1): the hidden shell stayed hidden; the unavailable engine produced no external HTTPS open. The original shell implementation was temporarily restored from the pinned base for that run, then the final fixes restored.

Linux build project list:

- `CCP.Core/CCP.Core.csproj` — exit 0, zero errors.
- `CCP.Avalonia/CCP.Avalonia.csproj` — exit 0, zero errors.
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` — exit 0, zero errors.
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` — exit 0, zero errors.

Final focused run: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter 'FullyQualifiedName~BrowserHostIntegrationTests|FullyQualifiedName~CompanionLinkLauncherTests|FullyQualifiedName~WebHostSandboxTests|FullyQualifiedName~WebHostProbeTests|FullyQualifiedName~SpiralEmbedViewTests'` (shared temporary output): exit 0, **51 passed, 0 failed, 0 skipped**. Production font registration resolved the initial setup limitation. The full Avalonia suite and real native browser/desktop integration were not run. `git diff --check` passed. Architecture guards were rerun after the final code changes and passed.
