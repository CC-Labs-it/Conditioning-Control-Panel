- Add WPF's recently focused app suggestions: eight distinct process names, newest first, keeping the first spelling on case-only revisits, excluding this process, and never persisted. Blocked apps are still suggested so an empty allow list can be repaired. Chips exclude listed names, preserve WPF text/tooltips, add once, and refresh on tab show, scope change and list commit.
- Sample process names every two seconds while the keyword engine is armed and entitled. Reuse the existing Windows resolver and X11 connection; X11 reads `_NET_WM_PID`, never a title. Unknown/native Wayland windows produce no suggestion. Test sandboxes cannot open the real desktop source.
- Dispatch tutorial and typed avatar comments through the existing AI and Core edited-phrase lookup. Keep WPF's AI-first/null-or-empty fallback, raw-pool fallback when the edited pool is empty, preset-first action merging, tutorial entitlement gate, record-before-dispatch ordering, and cooldown/settings bypass for demos.
- Track voice completion as well as bubble visibility. Comments wait 750ms, retry at most 16 times, then drop. Panic cancels pending AI replies and queued retries through the new `keyword-comments` registry entry; shutdown cancels them too. Existing tube speech/audio cleanup stops delivered comments.
- Keep existing base files and merge behavior into them. Adapted recent-chip rendering from trunk commit `119ec005fcfa4c05a3ced4bd390c84ecf2497a03`, and avatar comment routing from trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (file history commit `4f5dfe318eac6d00a7b0ab5944a964d2df58c79f`). The adaptation additionally reuses Core's edited phrase pool, waits for audio, and cancels late replies after panic.
- Keep exactly one `shell-awareness` ledger row and leave it **stub**. No WPF source was changed.

## Initial gap audit and remaining work

Reference pinned at base `86d81e715e77f31bff436614725457b9e7664c18`:
`ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs`, with the runtime calls traced into
`ConditioningControlPanel/Services/KeywordTriggerService.cs`.

- Recent-app collection and UI were absent: WPF `RefreshAwarenessSeenAppChips`, `AwarenessSeenAppChip_Click`, `GetRecentForegroundApps`, `RememberSeenApp`, and `SampleForegroundAppOccasionally`. Implemented here.
- Tutorial fires stopped after feed publication. WPF `FireDemoTrigger` also dispatches its synthetic actions. Avatar comment dispatch is implemented here, along with the same action for typed matches. WPF passes null OCR words to the demo dispatch, and `case HighlightAction` requires a nonempty word list: a visible demo highlight is not missing behavior. No unused highlight surface was imported.
- OCR capture, match confirmation, source-side ignore-own-UI/loop protection and start/stop from master/OCR toggles remain absent. The settings editors exist but cannot drive an absent ScreenOcr service. Linux keyboard input also remains absent; Windows retains its existing shared keyboard hook.
- Live OCR highlight windows, applying a new colour to existing windows and immediate capture-affinity changes remain absent. WPF `ChkAwarenessHighlightVisibleInCapture_Changed` calls `App.KeywordHighlight.RefreshCaptureVisibility`; these are live calls, not dead code.
- Other keyword actions remain incomplete: visual effects, haptics, progression/quest/achievement credit, session extension and Chaster time. This unit restores the comment action alongside existing audio, without claiming those dispatch paths work.
- WPF's `OnPresetsChanged` refreshes cards and the trigger drawer across service callers. Avalonia still creates separate preset-service instances; local editor close/card actions refresh, but cross-instance notifications remain missing.
- The v2 observer, reaction arbiter, activity ledger, and delivered-keyword external-line ledger write remain dependencies on the unported v2 runtime. Legacy title reactions remain as on the base.

The full runtime/overlay/source set does not fit this unit's 1,000-added-line budget. The visible app-picker and tutorial/comment gaps are completed; remaining work is explicitly recorded. This is not a pure dependency NOOP and does not use P122: live WPF callers still reach the missing runtime behavior.

## Assumptions

- On Linux, sample process names on an armed-engine timer because the WPF input-driven sampler has no Linux keyboard/OCR producer on this base. The two-second interval, eight-name bound, exclusion rules and in-memory lifetime follow WPF; collecting process names also makes the existing picker usable there. No title is read for this purpose.
- Use WPF's null-or-empty AI result test for keyword comments, distinct from the legacy title reaction's whitespace test. AI exceptions are caught without a canned response, as in WPF.
- Cancel pending comments on panic even though the WPF retry timer can outlive its tube-silence pass. This preserves the head's established rule that panic leaves nothing queued to speak.
- Retain the base's first-enabled audio/action ordering, settings semantics, and consent/privacy gates. No Windows runtime behavior is claimed from Linux compilation.

## Verification limits

.NET 10.0.402 on Linux; all tests use `DiffEngine_Disabled=true` and isolated user-data profiles. The original snapshot was clean but at a different commit, and its fetch failed because Git metadata was read-only. The authorized clone fallback was verified clean at the exact required base before creating the feature branch.

- Regression proof with old wiring restored: the recent-app and tutorial-comment tests both failed (exit 1): expected app names but got an empty list; expected one comment but got none. Restoring the wiring makes both pass unchanged.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AwarenessFinishRuntimeTests`: exit 0, **5 passed**, 0 failed/skipped.
- `dotnet build` Core, Avalonia, and all eleven Linux test projects named in `.github/workflows/build.yml`: exit 0, **0 errors** each. Includes Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot. Auxiliary test builds used `BuildProjectReferences=false` after Core/head outputs existed. Release, single-node and SDK hardlink-copy flags controlled output size.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed, 429 XAML references checked against 1728 head types / 1923 Core types. The first sandboxed invocation could not create temporary files; it was rerun with write access.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, **2874 passed, 0 failed, 5 skipped**, total 2879. The existing Vosk integration tests skipped because the unavailable speech model is absent; no tests were weakened or newly skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AwarenessFinish|FullyQualifiedName~AwarenessAppListEditorTests|FullyQualifiedName~AwarenessPulseFeedTests|FullyQualifiedName~AwarenessStatusPulseTests|FullyQualifiedName~KeywordAudioRuntimeTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0, **24 passed, 0 failed/skipped**, 21 seconds. Includes both new UI tests. Bundled production fonts worked here; no font-related tests were skipped. An initial comment UI test failed because headless setup has no desktop lifetime/window list; providing its shown tube through the resolver seam fixed the setup without changing its assertions.
- `timeout 180 dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (with the same single-node/hardlink flags): exit **124**, timed out in WPF temporary-project compilation after existing warnings. No successful WPF test-project compile or Windows execution is claimed.
- Added-line count against the specified base at final staging: **605**, below the 1,000-line cap. `git diff --cached --check` passed.
- No physical desktop, global keyboard hook, native X11 process read, live AI provider, physical audio device, Windows runtime or real user profile was exercised. AI tests use an in-process fake; audio uses a captured callback. Native Wayland process discovery remains unavailable. No render-all run or visual-verification status is claimed.
