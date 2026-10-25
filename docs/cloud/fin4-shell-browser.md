# fin4-shell-browser

- Added WPF same-window popup policy to the existing browser card/pop-out host.
  Ads are consumed; other requests navigate the same host. The HypnoTube partner bypass
  uses the live page and exact host/subdomain matching. Sandbox/navigation fences still apply.
- Added the shared Core popup rule and focused rule/shell tests.
- Added HTTPS-only external fallback when no engine was constructed, through the existing
  sandbox-aware ExternalOpener. Offline refusal runs first.
- Added tester steps; kept the single `shell-browser` row `stub` with an honest Missing list.
- No new surface: existing WebHost/pop-out panic exemption applies (Browser.cs:3044-3046).
- Source: WPF at base `c1f3bc13262363f18d69f3cde22309d599437f3c`.
  Fetched read-only trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`;
  no trunk code imported, so no imported trunk commits to credit.

## Remaining WPF gaps and dependencies

Compared the cited Browser.cs and BrowserService.cs with the head. These are live WPF
behaviors, so P122 does not apply:

- Selected-monitor mirroring (Browser.cs:2733-2760): no ScreenMirror host in Core/head;
  needs platform capture/mirroring rather than a second browser.
- Native 0.5 navigation / 1.0 fullscreen zoom and restore (Browser.cs:415,615,714,
  2865-2866,2935,3030): NativeWebView 12.1.0 exposes no zoom API.
- DOM-parsed avatar video autoplay with completion fallback, disarm, 1500 ms delay,
  20 x 500 ms video search and user-unblocked fullscreen retry (Browser.cs:770-917).
  NativeWebView has no DOMContentLoaded event.
- Playlist autoplay: 1500 ms hydration delay, 20 x 250 ms button search, dynamic audio
  binding, start/end reporting and 3 s fallback (Browser.cs:942-1015).
- Always-on media reporter and BrowserMediaService ownership: user/app sessions, slot
  arbitration, outgoing-pagehide suppression, progress, heartbeat, hard-stop and cool-off.
  WPF BrowserMediaService alone is 512 lines; no equivalent exists in Core/head.
  AutonomyScheduler's browser comment is not a media host.
- Remote/companion/autonomy takeover callers and stop paths (pause media, exit page/forced
  fullscreen). NavigateToUrlInBrowser has no live caller in the head; scripts alone would
  not wire ownership or completion.
- Audio-sync injection, connect-after-navigation rearm and detected/state/seek/ended
  message routing (Browser.cs:79,134-148,1018-1123) need the browser haptic bridge.
- Initialization/readiness, bounded 60 x 250 ms wait, retry, crash teardown/rebuild and
  asynchronous-failure external fallback (Browser.cs:46-305,438-485). HasEngine denotes
  control construction, not adapter readiness.
- Document-created/iframe fullscreen and mute, frame messages and focused-native-view
  Esc/F11: completion-time scripts lack WPF early/iframe hooks.
- Native JS and real desktop stacking remain unproven headless.

A complete media host/reporter with haptic and takeover integrations, platform mirroring
and native API extensions cannot fit this 1000-added-line unit. Independent popup and
unavailable-engine fallback behavior is implemented; remaining gaps stay explicit.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore -m:1`:
  exit 0, 0 warnings/errors on final explicit build.
- `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release
  --no-restore -m:1`: exit 0, 0 errors. A preceding multi-worker build had MSB4166;
  the single-worker retry passed.
- All ten other Linux test projects named in build.yml compiled, exit 0 / 0 errors:
  CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot,
  Core.Content, Core.Settings and Core.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The first attempt
  lacked temporary-file write access; rerun with workspace execution permission passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`, with DiffEngine_Disabled=true:
  exit 0, 2842 passed, 5 existing Vosk-model skips, 0 failures (2847 total).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter
  'FullyQualifiedName~BrowserModNavigationTests|FullyQualifiedName~BrowserPopoutTests|FullyQualifiedName~BrowserFullscreenTests'`,
  with DiffEngine_Disabled=true: exit 0, 22 passed, 0 skipped/failed.
- Native popup dispatch, external system-browser launch, JS and desktop stacking were
  not run on a real desktop. Headless tests use profile sandboxes and captured launches.
- The test-plan coverage script does not exist on this base; tester steps were added anyway.
- No tests were weakened or removed. No new surfaces require panic registration.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings,
  0 errors. WPF/Windows code compiled but was not run on Linux.

## Assumptions

- Reuse WebHost and ExternalOpener; avoid a new browser-service abstraction.
- Opt popup policy into the shell only; game/embed hosts retain their policy.
- Use WPF HTTPS fallback for link navigation when no engine exists; site/home selection
  retains the existing unavailable-engine panel.
- This fallback does not claim asynchronous crash/readiness or document-created parity.
