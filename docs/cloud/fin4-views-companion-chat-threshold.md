# fin4-views-companion-chat-threshold

- Dependency-only analysis; no product, test, or ledger changes. The single
  `views-companion-chat-threshold` row remains `stub`.
- Base verified at `cb8923d880b209496994524e6ba87063b7befe2c` (all WPF references below
  are pinned to that commit). The supplied snapshot was read-only and at a different
  commit; work used the explicitly requested fallback clone.
- Read the row, WPF `ChatThresholdView.xaml` and `.xaml.cs`,
  `Runtime/ChatThresholdRuntimeVm.cs`, and `Runtime/CompanionLinkLauncher.cs` under
  `ConditioningControlPanel/Views/Controls/Companion/` against the existing Avalonia
  implementations and tests. State selection, last-three-turn projection, AI badges,
  last-heard copy, brain send/refusal path, transcript, full-chat and Engine Room
  navigation, unlock, assistant watch chips, remote-controller guard, HTTPS-only
  external fallback, and native scroll chaining already have head implementations.
- No trunk code imported. Read-only reference `parity/trunk` fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its Core/Avalonia tree has no
  BrowserMedia service/reporter to reuse.

## Remaining WPF behaviour

1. A valid, user-clicked watch chip replaces the media session with
   `MediaOwner.AvatarLink`, `takeover: true`, before embedded navigation, even if a
   previous session exists. See WPF `Runtime/CompanionLinkLauncher.cs:47`.
2. If embedded navigation returns false, retire that claim with
   `OnMediaStopped("embedded-browser-unavailable")` before the existing HTTPS-only
   external fallback (`:58`). This needs real ownership bookkeeping, not a flag.
3. Honour `autoPlayFullscreen: true`. WPF `MainWindow/MainWindow.Browser.cs:769-924`
   arms a once-only DOM-content-loaded/completion race, disarms failed navigation,
   waits 1.5 seconds, then retries video discovery 20 times at 500 ms. It unmutes,
   plays, requests fullscreen, retries fullscreen on `playing` after autoplay denial,
   and handles ended/double-click/fullscreen-exit without conflating fullscreen exit
   with media stop. Native page fullscreen support alone does not perform this.

These are live WPF behaviours (the chip calls the launcher); P122 does not apply.

## Dependency and scope decision

The missing feature is **wire-shell-browser-b-next: BrowserMedia lifecycle and
parse-time autoplay/fullscreen**, already named in the base ledger row.

Reproduction on the pinned base:

```sh
rg -n 'BrowserMedia|AvatarLink' CCP.Avalonia CCP.Core
rg -n 'autoPlayFullscreen|ArmVideoAutoPlay|DomContentLoaded' CCP.Avalonia
wc -l ConditioningControlPanel/Services/Browser/BrowserMediaService.cs
```

The first search finds no service or owner implementation; the only Core mention
is an autonomy comment. `CompanionLinkLauncher.OpenAsync` explicitly defers ownership;
`MainShellWindow.Browser.cs:190-193` logs and ignores the autoplay request.

`WebHost.axaml.cs` already has `InvokeScriptAsync`, `WebMessage`, and completion
signals, and `WebHostMedia.cs` supplies mute. Older shell comments saying there is
no script channel are stale. However, `WebHost.axaml.cs:170` explicitly lacks a
script-at-document-creation equivalent and exposes no DOM-ready event. A completion-
only script would retain the WPF cold-load latency bug that the parse-time hook fixes.

A correct shared host is larger than this unit's 1,000-added-line budget. The WPF
service alone is 512 lines and includes queue acquisition/release, generation-safe
hard stops, settings-based interruption and new-video gates, 5-second heartbeats,
60-second load grace, 20-second playback silence timeout, and duration + 15 seconds
hard stop capped at one hour. The always-on reporter is another ~125 lines
(`Services/Browser/BrowserService.cs:332-456`): audible/player-size filtering,
5-second progress, 2-second pause debounce, 3-second element rescanning, and pagehide
handling. Autoplay/teardown is ~170 more lines (`MainWindow.Browser.cs:769-940`).
That already approaches the cap before adapting App/WPF dependencies into Core,
adding startup/shutdown/panic and queue consumers, document-ready transport,
message parsing, navigation-failure cleanup, or focused lifecycle/timing/head tests.
A standalone AvatarLink flag would strand ownership or omit these WPF guarantees.

Per the unit's pure-dependency rule, no partial host or product change is made.
After the dependency lands, wire the launcher's pre-navigation claim and failed-
navigation release into that host and prove those calls with focused tests. Keep
`stub` until those behaviours and real autoplay are reachable through Her Room.

## Verification limits

- .NET SDK 10.0.402; all commands used Release configuration.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` for every Linux test csproj named in
  `.github/workflows/build.yml`: exit 0, 0 errors for each of CatalogueStartup,
  Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, main Avalonia,
  Core.Content, Core.Settings, and Core.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (424 XAML
  references checked). The first unprivileged attempt could not create temporary
  files; the rerun with writable temporary storage passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj
  -c Release --no-build`: exit 0; 2,829 passed, 0 failed, 5 skipped (Vosk model
  unavailable; no test was altered). The initial build matrix exhausted temporary
  disk space and caused 53 Core-test failures; after cleaning generated build
  outputs, the complete rerun passed.
- `DiffEngine_Disabled=true dotnet test
  Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter
  'FullyQualifiedName~ChatThresholdLinksTests|FullyQualifiedName~CompanionLinkLauncherTests|FullyQualifiedName~HerRoomChatMemoryTests|FullyQualifiedName~CompanionCardsRowTests'`:
  exit 0; 11 passed, 0 failed, 0 skipped. Shipped font bootstrap works on this base.
- To fit the temporary disk, generated test outputs were removed between test-
  project builds; all initially disk-blocked Linux builds were rerun successfully.
- `test-plan-coverage.sh` is absent on this base (attempt returned 127); no ledger
  row was added or promoted.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings,
  0 errors; WPF head and test assembly compiled. Windows execution, real browser engines,
  live account/provider integration, and render-all were not run. No new runtime
  claim or P122 promotion relies on those checks.
- `git diff --check`: exit 0. Only this analysis document is changed.

## Assumptions

- Apply the explicit infrastructure fallback after the read-only fetch failure.
- Apply the pure-dependency NOOP rule rather than duplicating the browser host or
  claiming parity from plain URL navigation.
- No new behaviour or surface means no new tests, panic registration, tester step,
  or ledger promotion is appropriate.
