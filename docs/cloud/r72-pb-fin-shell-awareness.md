# Shell awareness part B: recent apps

Required base: 884cbc282726dbb2f4b2bb4c2f21078c442e5b29.
The read-only snapshot started at aa9918833722f057079e455167cb3d1b14462340
on work, clean. Fetched the requested lane and checked out the required commit
in /tmp/ccp. No requester-local changes are present.

## Base check and scope

Checked every part B item against the required base before editing. The full row
cannot reach wired within 600 changed lines. This commit delivers the recent-app
picker as a complete user path; shell-awareness remains stub, never verified.
All paths below are repository-relative. WPF shell =
ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs; WPF service =
ConditioningControlPanel/Services/KeywordTriggerService.cs.

| Item | Base finding | This commit and WPF specification |
| --- | --- | --- |
| Recent-app picker | Missing: CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs:331 explicitly leaves the row empty; .axaml:566-573 already has label/panel | Ported: Platform/RecentForegroundApps.cs:13 keeps eight distinct newest-first process names, first spelling retained, own/unknown excluded. WPF service:489-520,582. Platform/KeywordTriggerHead.cs:22,30,48 samples every 2s while running and stops on shutdown/master-off; WPF service:618-622 throttles foreground sampling. Platform/KeywordTriggerHead.cs:93 and Platform/X11ActiveWindow.cs:89 read only foreground PID/process, not titles. WPF service:527-582 uses foreground PID/process. Native Wayland stays unknown. |
| Picker refresh and addition | Missing: same base tab:331 | Ported: Views/Tabs/AwarenessTabView.Apps.cs:12 filters already-listed names through existing Core rules; :30 saves a clicked app, rewrites box and refreshes chips. Tab .axaml.cs:151,332,356 refreshes on seed/show, scope change and list commit. WPF shell:575-642,659-698. Existing copy reused; no new translation keys in any of the ten languages. |
| Last Detected feed and live engine pulse | Missing: base tab .axaml.cs:149,186 derives status from preference; no fire log in base KeywordTriggerHead.cs:148-180 | Still part B: current tab .axaml.cs:149,171 and runtime KeywordTriggerHead.cs:183. WPF shell:144-180,370-383. |
| Demo feed, quest and achievement recording | Missing: base runtime:181-192 explicitly defers it | Still part B: current runtime:205-212 only delivers the comment. WPF service:426-428,1278,1340, RecordFire:135. |
| OCR and Linux keyboard lifecycle | Missing: base tab:256 and runtime:27 only install Windows hook | Still part B: tab:256 and runtime:36. WPF shell:443-502; WPF service owns keyboard/OCR dispatch. X11 PID sampling here is not a keyboard source. |
| Highlights, recolor and capture affinity | Missing: base tab:284,410 stores preferences only | Still part B: tab:284,410. WPF shell:502-540 and WPF service:1340. |
| Remaining response actions | Missing: base runtime:148-179 handles audio/comments only | Still part B: runtime:188-200; WPF service:1340-1546. |
| Cross-instance presets and v2 observer/arbiter/delivered-keyword ledger | Missing: base Views/Tabs/AwarenessTabView.Presets.cs:27 refreshes only this view; Platform/KeywordAvatarComment.cs:24 uses legacy delivery | Still part B: same files unchanged. WPF shell:114; WPF service:1660; ConditioningControlPanel/Services/Awareness/AwarenessObserver.cs. |

Already present and retained: app scope normalization and settings/tier gates,
CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs:111,193,345 and Core
Services/Awareness/AwarenessAppScopeRules.cs:14,31; WPF shell:41,393,659-698.
Part A comment behavior is unchanged (Platform/KeywordAvatarComment.cs:24;
WPF service:1548,1621).

No WPF or Core behavior changed. The timer runs on the UI dispatcher, sharing the
existing X11 connection and error-handler discipline. No desktop titles, keystrokes,
command lines or app history are persisted by the sampler. The clicked list is
persisted through the existing settings service. Suggestions remain session-only.

## Validation

- SDK 10.0.100 installed using dotnet-install --channel 10.0 with a pinned version
  after the channel endpoint returned 403. Disk-space failures during setup/test
  output copying were resolved by removing duplicate temporary Git objects and SDK
  extraction directories, then retrying the ordinary builds.
- dotnet build CCP.Core: 0 errors. dotnet build CCP.Avalonia: 0 errors.
- dotnet build Tests/CCP.Avalonia.Tests --no-restore: 0 errors.
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 existing skips, 0 failures.
- Affected Avalonia run: 25 passed, 0 skipped. Filter: AwarenessRecentAppsTests,
  AwarenessAppListEditorTests, AwarenessFinish, TestIsolationTests.
- The three new tests exercise the full ring order (strengthened after a duplicate
  mutation survived the initial first-item/count assertion), actual two-second timer and stop gate,
  and the chip-to-persisted-settings path plus show/mode/commit refresh.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- Full Avalonia suite compiled, not run. No live desktop, native foreground PID,
  Wayland compositor, AI provider, audio device or OCR capture validation.
- Fourteen deliberate mutations failed their focused tests, then were restored:
  capacity, duplicate removal, own-process exclusion, newest-first ordering,
  interval, actual timer tick delivery, timer stop, inactive-engine gate,
  listed-app filtering, save, click addition, mode refresh, commit refresh,
  show/seed refresh. The first duplicate mutation survived the initial test;
  after strengthening the full-order assertion it failed, and the restored
  three-test baseline passed again.
- Mutation runs rebuild the changed production assembly and use the unchanged
  compiled tests. The final validation rebuilds both head and tests from restored
  sources, then repeats the complete affected filter.
- Restored final run: all three builds had 0 errors; all 25 affected Avalonia
  tests passed; Core guards and avalonia_docs self-check passed again.
