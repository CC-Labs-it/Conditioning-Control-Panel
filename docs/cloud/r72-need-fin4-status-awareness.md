# Status and awareness re-cut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad.
Old lanes inspected: e77662b7d324..136b6170c4db and ecfd104a8128..0f7bbba6b50b.
The snapshot initially held aa9918833; clean work was reset to the required base.
No WPF behavior, safety path, Scrolller code or localization keys changed.

WPF paths below start at ConditioningControlPanel/. Avalonia paths start at CCP.Avalonia/.

| Behavior | Disposition and head evidence | WPF specification |
| --- | --- | --- |
| Repeated status refresh preserves phase; sine midpoint and full reverse cycle | Runtime already at Views/Windows/MainShellWindow.TabFxTakeoverLabStatus.cs:246; old assertions ported at Tests/CCP.Avalonia.Tests/StatusPulseLifecycleTests.cs:60 | MainWindow/MainWindow.TabFxTakeoverLabStatus.cs:288,292 |
| Ambient focus/minimize/motion/tier gates, capped sibling glow, mod retint and close cleanup | Already at Views/Windows/MainShellWindow.TabFxTakeoverLabStatus.cs:51,93,140,202,232; base wins over older lane | MainWindow/MainWindow.TabFxTakeoverLabStatus.cs:89,254,280 |
| Awareness status entry point | Already at Views/Tabs/AwarenessTabView.axaml.cs:177; this is master-switch intent on WPF too | MainWindow/MainWindow.Awareness.cs:78,375 |
| App-list parsing, case/suffix matching, Enter/blur commit | Already at CCP.Core/Services/Awareness/AwarenessAppScopeRules.cs:10 and Views/Tabs/AwarenessTabView.axaml.cs:336,346; old AwarenessAppList duplicate omitted | MainWindow/MainWindow.Awareness.cs:659,668,675; Services/KeywordTriggerService.cs:594,601 |
| Twenty newest fires and enabled-action snapshots, omitting internal XP | Ported old KeywordFireLog to CCP.Core/Services/KeywordFireLog.cs:12; WPF stays unchanged | Services/KeywordTriggerService.cs:64,72,110,135 |
| Keyboard records and notifies after acceptance/cooldowns | Ported Platform/KeywordTriggerHead.cs:167; source, timestamp and action snapshot retained | Services/KeywordTriggerService.cs:1196 |
| Entitled tutorial synthetic fire bypasses master/cooldowns, without saved audio | Ported history/notification only at Platform/KeywordTriggerHead.cs:183; existing caller Tours/TutorialService.cs:78 | Services/KeywordTriggerService.cs:393,426 |
| Avatar comment execution | Covered by 884cbc282, branch avalonia-port/cloud-r72-fin-shell-awareness: Platform/KeywordAvatarComment.cs:24 and KeywordTriggerHead.cs:174,183. Do not duplicate | Services/KeywordTriggerService.cs:1283,1439,1548 |
| Recent foreground apps | Covered by 263b76968, branch avalonia-port/cloud-r72-pb-fin-shell-awareness: Platform/RecentForegroundApps.cs:7 and Views/Tabs/AwarenessTabView.Apps.cs:15 | MainWindow/MainWindow.Awareness.cs:575,622 |
| Toggle preservation and app tooltip | Covered by ab9af725f, branch avalonia-port/cloud-r72-row-views-tab-awareness: Views/Tabs/AwarenessTabView.axaml.cs:129 | MainWindow/MainWindow.Awareness.cs:480 |
| Status documentation | f1fd17597, branch avalonia-port/cloud-r72-fin4-shell-tab-fx-takeover-lab-status, changes docs only; missing assertions remain in scope here | MainWindow/MainWindow.TabFxTakeoverLabStatus.cs:292 |

## Part B

The remaining work needs its own localized UI and action/lifecycle tests. This commit
keeps the bounded runtime history and notification contract self-contained.

- Five-row feed, count, relative times, action chips and attach/detach subscription:
  WPF MainWindow/MainWindow.Awareness.cs:133,144,187,283,360;
  current Views/Tabs/AwarenessTabView.axaml:193 is still the empty placeholder,
  Views/Tabs/AwarenessTabView.axaml.cs:98 does not subscribe to TriggerFired.
- OCR capture, live highlight/recolor/capture affinity and other trigger actions:
  WPF MainWindow/MainWindow.Awareness.cs:443,502 and Services/KeywordTriggerService.cs:1340;
  current Views/Tabs/AwarenessTabView.axaml.cs:255,286,416 stores settings only.
- Demo dispatch beyond recording and quest/achievement credit:
  WPF Services/KeywordTriggerService.cs:428,1278;
  current Platform/KeywordTriggerHead.cs:183 records only. Avatar dispatch is covered above.
- V2 observer/ledger lifecycle is outside these old lanes' delivered work:
  current Views/Windows/MainShellWindow.Awareness.cs:8 records the gap.

## Validation

Focused tests: KeywordFireLogTests, KeywordAudioRuntimeTests, StatusPulseLifecycleTests.
The Core tests assert bounded newest-first order, stable snapshots, enabled-only action
keys, no XP chip, and legacy null actions. Head tests assert record-before-notification,
master/entitlement/own-focus/cooldown/stop gates and tutorial bypass without audio.
The phase test uses stepped time, checking identity, midpoint, peak and full reversal.
Command results and mutation fail-proofs are recorded below after execution.

- dotnet-install --channel 10.0: ci.dot.net lookup denied (403).
  Fallback dotnet-install --version 10.0.100 --azure-feed https://builds.dotnet.microsoft.com/dotnet succeeded.
- dotnet build CCP.Core: 50 warnings, 0 errors.
- dotnet build CCP.Avalonia --no-restore: 0 warnings, 0 errors on the sequential retry.
  Initial concurrent commands collided on an intermediate Avalonia DLL; no source error.
- dotnet build Tests/CCP.Avalonia.Tests: 395 warnings, 0 errors.
- dotnet test Tests/CCP.Core.Tests: 4637 passed, 6 existing skips, 0 failures.
- Affected Avalonia filter (KeywordAudioRuntimeTests and StatusPulseLifecycleTests):
  9 passed; 3 shell tests stopped at Segoe UI Black glyphTypeface creation.
  The phase assertions are compiled, not run. No tests were weakened or skipped.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.

Fail-proofs (temporary mutations, all reverted):

- Omit keyboard and demo Fires.Record: both new runtime tests failed (2 failed, 0 passed).
- Omit ring insertion: both Core history tests failed (2 failed, 0 passed).
- Change capacity from 20 to 21: bounded-history assertion failed (1 failed, 1 passed).
- Replace action snapshot with an empty list: action assertion failed (1 failed, 1 passed).
- No phase fail-proof is claimed: shell construction stops at the font error before those assertions.

After restoring all mutations, sequential builds passed: Core 50 warnings/0 errors,
Avalonia 201 warnings/0 errors, Avalonia tests 144 warnings/0 errors.
Restored focused tests passed: Core history 2/2 and head runtime 2/2.
Final guards and docs checks passed again. No live desktop verification is claimed.
