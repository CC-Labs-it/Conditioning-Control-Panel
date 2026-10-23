# fin-shell-browser

- Load the active mod's exact default URL on the first browser prompt or pop-out. Explicit site clicks and Reload still use the selected site's root homepage.
- Match WPF #867 site-radio synchronization: preserve a known live page when the mod offers BambiCloud; otherwise select the mod's site and navigate an already-loaded browser to the mod default. The visibility override does not change the mod's site. Offline switches never navigate.
- Restore a hidden or minimized shell before opening an embedded link (#1138), reusing the existing tray restoration path.
- Reassert the existing pink, spiral and corner-GIF overlays after fullscreen entry/activation and every 500 ms (#1041). Stop the timer on escape, page exit, session stop and window close. There is no new surface to register for panic; the user's own browser page remains exempt, matching WPF.
- Add focused browser regressions. The pre-fix run reached assertions: **5 failed, 2 passed**, including hidden/minimized links, first-load mod URL, mod switching with the visibility override, and external-page radio synchronization. No tests were weakened or removed.
- Keep the single `shell-browser` ledger row **stub** with explicit remaining gaps.
- Reference sources: in-tree WPF at base `a5c551abff5e71c6d3122accf1df2ccc22ec20bb`; fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk files were imported or overwritten. Existing imports remain credited to `e3109afdf` / `376305e62`; the missing DOM-first autoplay behavior originates in trunk `daf734417`.

## WPF audit

Scope: the browser regions of `ConditioningControlPanel/MainWindow/MainWindow.Browser.cs`, not the independently ledgered profile editor in the same file. Supporting references read: `MainWindow.xaml.cs:2595-2605,3140-3156`, `MainWindow.DeeperTab.cs:505-530`, `Services/Browser/BrowserService.cs`, `Services/Browser/BrowserMediaService.cs`, and `Services/Notifications/OverlayService.cs:85-150,2935-3045`.

| Behavior missing at the base | Result in this branch |
| --- | --- |
| Initial browser default and mod synchronization (`Browser.cs:217,533-568`, shell mod handler) | Implemented through prompt/pop-out and the live mod-change subscription, including override and offline cases. |
| Restore hidden/minimized shell before link focus (`Browser.cs:338-375`) | Implemented; existing pop-out focus still wins. |
| Fullscreen overlay notification / forced refresh (`BrowserService.cs:556-567`, overlay #1041) | Implemented for this head's existing pink/spiral/corner-GIF windows using the existing platform raise seam, preserving WPF order. No new OS-specific code. |
| Fullscreen selected-monitor mirroring (`Browser.cs:2733-2759`) | Missing: no Avalonia ScreenMirror service. Respecting the global monitor picker needs a real mirror implementation. |
| Initial/navigation 50% zoom, fullscreen 100%, previous zoom restore (`Browser.cs:419,613,2864,3026`) | Missing: NativeWebView/WebHost exposes no zoom factor. CSS scaling would change page behavior and is not a native zoom port. |
| Video autoplay on DOM parse with one-shot completion fallback (`Browser.cs:774-923`) | Missing: WebHost exposes completion but no DOM-parsed seam. WPF waits 1500 ms after the first event and retries player discovery up to 20 times. Completion-only injection would reintroduce the cold-load delay fixed by `daf734417`. |
| BambiCloud playlist autoplay (`Browser.cs:943-1018`) | Missing: completion-triggered, 1500 ms hydration delay, 20 button polls at 250 ms, current/future audio events, 3 s start fallback, takeover bookkeeping. No Avalonia caller currently requests autoplay. |
| Always-on media reporting / ownership (`BrowserService.cs:332-455`, `BrowserMediaService.cs`) | Missing: document-created/frame hooks, real-player filtering, 5 s progress heartbeat, 2 s pause debounce, ownership/arbitration and end/watchdog publication. The WPF ownership service alone is 512 lines and depends on additional head services. |
| Audio-sync injection, rearm on late haptic connection, messages (`Browser.cs:132-154,1023-1360`) | Missing: browser-side scripts plus DSP/chunk progress/ready bridge; Core audio processing already exists but does not provide the browser lifecycle. |
| Remote/companion/autonomy takeover and teardown (`Browser.cs:926-939,3043-3141`) | Missing: BrowserMedia ownership, screen-owning-host refusal, remote overlay reveal and stop/home handoff; companion/autonomy callers also require the autoplay path. |
| Native init/readiness/failure recovery (`Browser.cs:36-316,380-487,643-770`) | Missing: equivalent browser-ready/process-failed signals, stale-host retry, bounded bring-up wait and HTTPS external fallback. WebHost currently has availability/fallback UI, not this WPF recovery lifecycle. |
| Supporting browser engine parity (`BrowserService.cs`) | Missing: same-window popup handling, native document-created/subframe fullscreen reporting and browser-wide mute (document mute cannot reach cross-origin frames). Existing JS escapes run only after completion; native-focus key delivery remains unproven. Ad/request filtering and browser-enhancement/discovery bridges also remain separate engine/Deeper dependencies. |

These are live WPF behaviors, so P122 does not apply. Pinned proof command:

```sh
git grep -n 'NavigateToUrlInBrowser\|EndWebVideoTakeover\|PlayHypnotubeFromRemote\|StopBrowserVideoFromRemote' a5c551abff5e71c6d3122accf1df2ccc22ec20bb -- ConditioningControlPanel
```

Live matches include `AvatarTube/AvatarTubeWindow.Speech.cs:1480`, `Views/Controls/Companion/Runtime/CompanionLinkLauncher.cs:50`, `Services/AutonomyService.cs:1254`, `Services/Browser/BrowserMediaService.cs:370` and the remote service. This is not a pure dependency NOOP: reachable homepage, focus and overlay gaps were fixed. The remaining browser/media lifecycle and adapter work exceeds this unit's 1000-added-line cap; it remains explicitly unported.

## Verification limits

All builds below used `dotnet build <project> -c Release -m:1 -v:q`. The WPF project additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Project | Exit | Error count |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 |

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed` (424 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: exit 0, **2831 passed, 0 failed, 5 skipped** (2836 total). The existing five Vosk tests skip because the model is unavailable; its download is blocked in this environment.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~BrowserModNavigationTests|FullyQualifiedName~BrowserPopoutTests|FullyQualifiedName~BrowserFullscreenTests|FullyQualifiedName~HomeMarqueeTests'` with `DiffEngine_Disabled=true`: exit 0, **33 passed, 0 failed, 0 skipped** (3 m 14 s).
- Earlier post-fix browser/Home-card run: exit 0, **31 passed, 0 failed, 0 skipped**. The final run also covers the two added first-load tests and the real 500 ms dispatcher-timer check.
- `git diff --cached --check`: exit 0. Exactly one `shell-browser` ledger row remains.
- Initial build sweep exhausted the worker's writable disk while duplicating test outputs. Only generated outputs were removed; the complete sequential rerun above passed.


- SDK: .NET 10.0.402. Channel discovery failed against a blocked discovery endpoint; explicit release installation succeeded.
- The supplied font limitation did not reproduce in the pre-fix filtered run: it reached behavioral assertions in the sandbox. Focused post-fix suites are therefore run here.
- All headless tests use the existing `CCP_USERDATA_DIR` test sandbox; no native web engine or real desktop is accessed.
- Native JavaScript, reparenting, dual monitors, focused-native-view Esc/F11 delivery and actual stacking/activation are not exercised headlessly. Windows platform code compiles as part of Avalonia but cannot run on this Linux worker. The WPF test project compiled successfully with the flags above but was not run.

## Assumptions

- Follow WPF's current live mod handler: a mod hiding BambiCloud navigates to its exact default even when the override reveals the radio; offering BambiCloud preserves a live known-site readout.
- Use existing tray restoration for a hidden shell, preserving its existing avatar restoration behavior.
- Preserve native zoom and DOM timing as explicit gaps rather than substitute CSS zoom or late autoplay. No `wired` or `verified` claim is made.
