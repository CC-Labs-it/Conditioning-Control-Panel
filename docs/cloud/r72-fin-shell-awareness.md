# Shell awareness re-cut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Old lane: de16f02dfc76.
Only shell-awareness is advanced. The complete old diff exceeds 600 changed lines.
Part A ports the comment path and its safety gates. No WPF changes or new copy keys.
References below use repository-relative paths; WPF service means
ConditioningControlPanel/Services/KeywordTriggerService.cs and WPF shell means
ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs.

| Old behavior | Base / delivered evidence | WPF specification |
| --- | --- | --- |
| Settings, entitlement and cooldown/color editors, old AwarenessTabView.axaml.cs:118 | Already: CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs:111,192,365 | WPF shell:41,393,443,480 |
| Enter/blur normalization, old AwarenessTabView.axaml.cs:351 | Already: same base file:336,346 using Core AwarenessAppScopeRules | WPF shell:659,668,675 |
| Preset cards/editor/advanced/tutorial, old AwarenessTabView.Presets.cs:27 | Already: CCP.Avalonia/Views/Tabs/AwarenessTabView.Presets.cs:27 and CCP.Avalonia/Tours/TutorialService.cs:78 | WPF shell:102, onboarding FireDemoTrigger WPF service:393 |
| Privacy-filtered consent/title reactions and legacy AI routing | Already: CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.Reactions.cs:32; native Wayland remains unknown | ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Reactions.cs:46; unchanged legacy path |
| First enabled merged comment, old Platform/KeywordTriggerHead.cs:226 | Ported: CCP.Avalonia/Platform/KeywordTriggerHead.cs:174 | WPF service:1283,1439 |
| Tutorial comment bypasses master/cooldowns but needs entitlement, old KeywordTriggerHead.cs:236 | Ported comment only: same new file:181; real caller CCP.Avalonia/Tours/TutorialService.cs:78 | WPF service:393-432; null OCR words draw nothing |
| AI-first, edited pool then raw fallback, old Platform/KeywordAvatarComment.cs:23 | Ported: CCP.Avalonia/Platform/KeywordAvatarComment.cs:24 | WPF service:1548-1615 |
| Busy bubble/audio, 750ms x16 retries, old KeywordAvatarComment.cs:55 | Ported: new file:57, using existing AvatarTubeWindow.axaml.cs:1424 SpokenAudioDone instead of old duplicate voice state | WPF service:1621-1655 |
| Panic and shutdown cancellation, old Views/Windows/PanicSurfaces.cs:45 and KeywordTriggerHead.cs:73 | Ported: CCP.Avalonia/Views/Windows/PanicSurfaces.cs:69 and Platform/KeywordTriggerHead.cs:40; shutdown caller App.axaml.cs:1268 | Stronger cancellation than WPF; current WPF service:1263 quiet also retained in new comment path |

## Part B

| Remaining old lane work | Old lane evidence | WPF specification / current gap |
| --- | --- | --- |
| Recent eight distinct newest-first process suggestions, process-only 2s sampling, own/list exclusion and refresh on show/mode/commit | CCP.Avalonia/Platform/KeywordTriggerHead.cs:24,151; Platform/X11ActiveWindow.cs:91; Views/Tabs/AwarenessTabView.Apps.cs:15 | WPF shell:575,622; base AwarenessTabView.axaml.cs:331 still empty |
| Five-row Last Detected feed, fire snapshots and runtime status pulse | CCP.Core/Services/KeywordFireLog.cs:12; CCP.Avalonia/Views/Tabs/AwarenessTabView.Pulse.cs:22; AwarenessTabView.axaml.cs:186; KeywordTriggerHead.cs:214 | WPF shell:144,375; base still uses preference at AwarenessTabView.axaml.cs:149 |
| Demo feed/quest/achievement recording | old KeywordTriggerHead.cs:250 only records feed; credit was already missing | WPF service:426,1278; comment delivery works, recording remains absent |
| OCR capture/matching/toggle lifecycle, Linux key source, live highlight/recolor/capture affinity, other actions and credit | Old lane ledger explicitly missing | WPF shell:443,502 and WPF service:1340; existing audio retained |
| Cross-instance preset notifications and v2 observer/arbiter/activity/delivered-keyword ledger | Old lane ledger explicitly missing; old KeywordAvatarComment.cs:68 | WPF shell:114; WPF service:1660; base incomplete |

The feed and recent-app work alone add substantial UI, platform, Core and test code.
Part A selects user-visible comments and cancellation; the picker and feed remain part B.
No live desktop, remote AI or audio-device verification is claimed; tests use injected sources.

## Verification

- .NET 10 installed with dotnet-install --channel 10.0 (SDK 10.0.403).
- dotnet build CCP.Core, CCP.Avalonia and Tests/CCP.Avalonia.Tests: zero errors.
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 pre-existing skips.
- Initial Awareness, PanicSurfacesTests and TestIsolationTests run: 44 passed, no skips.
- Restored focused run (new tests, PanicSurfacesTests, TestIsolationTests): 24 passed.
- Two later broad runs hit glyphTypeface setup failures in existing tests (Inter, then
  Consolas/Courier New); one hung and was terminated, the retry was bounded to 120s.
  This remains a broad headless verification limit; no tests were changed or skipped.
- Six new tests passed. Thirteen production mutations each produced a test failure:
  demo dispatch, typed dispatch, AI selection, edited pool, raw pool, panic cancellation,
  shutdown cancellation, post-panic quiet, voice busy, bubble busy, retry limit,
  retry delay and queued panic cancellation. Sources restored after every mutation.
- bash .github/scripts/core-guards.sh and python3 .github/scripts/avalonia_docs.py passed.
- The full Avalonia suite was compiled; only the affected 44 tests were run.
  No live desktop, AI provider, microphone or audio device was used.
