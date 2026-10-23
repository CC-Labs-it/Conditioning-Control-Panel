- Base verified at `e3593f618bd7ccb608761cdd18c580bf44e22a03`; used the authorized temporary clone after the supplied snapshot rejected FETCH_HEAD writes.
- Wire completed sessions and accepted received remote commands into the existing Core quest board, independently of tab visibility. Early stops, refusals and subsequent empty polls earn nothing.
- Seed perfect-week rewards using WPF's owned-skill gate, 7/14/30-day milestones, level scaling, paid-once latch and break reset. Grant before saving the latch. Reuse AchievementPopup with the same localized copy and skill art; register the popup with panic.
- Feed the persisted login-streak count into startup's weekly streak quest. The login-streak lifecycle itself remains a dependency, as explained below.
- Restore WPF calendar paint order: connectors below days, fix rings above days, shields above rings. Add focused headless coverage.
- Keep `shell-quests-tab` **stub**: this unit does not pretend unported producers or account recovery are complete.
- Imports: no parity/trunk code imported. Read-only reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; behavior adapted directly from WPF at the pinned base.

## Verification limits

- SDK: .NET 10.0.402.
- All 13 required Linux builds passed (Core, Avalonia, and every Linux test project from `.github/workflows/build.yml`), each with **0 errors**. Existing warnings remain. Commands: `dotnet build <project> -c Release -m:1 -nr:false -p:UseSharedCompilation=false`; copy-hardlink flags were also supplied. Core/head references were built first; subsequent test builds used `-p:BuildProjectReferences=false`.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:BuildProjectReferences=false`: exit 0, **2,826 passed, 0 failed, 5 skipped** (the existing Vosk speech tests; model unavailable and download documented as blocked). This includes all new perfect-week rules.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~QuestProducerTests|FullyQualifiedName~PanicSurfacesTests.EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered|FullyQualifiedName~PanicSurfacesTests.TheOrderingConstraintsHold|FullyQualifiedName~PanicSurfacesTests.EveryStarterIsRegisteredOrAllowlisted'` (compiled references reused): exit 0, **4 passed, 0 failed, 0 skipped**. Covers completed versus early-stopped sessions, startup's persisted streak feed, quiet accepted versus refused commands, subsequent empty polls, and the new safety-critical panic entry.
- Pre-fix proof: perfect-week tests against the no-op behavior exited 1 (**7 failed, 1 passed**); the producer test against unconnected hooks exited 1 (natural session completion left its daily quest incomplete). A later fake-server replay failure was corrected by modeling the real relay's command dequeue; the credit assertions remain.
- `bash .github/scripts/core-guards.sh`: exit 0, **All Core guards passed** (424 XAML references checked). `git diff --check`: exit 0.
- `PerfectWeekPopupTests`, calendar-layer coverage in `QuestsTabStreakFixTests`, and existing quest view/art/fix classes **compiled but were not run** under this worker's documented font-creation limit. The supervisor's GitHub check must run these. No UI test was skipped or weakened in source.
- Windows-specific Avalonia code compiled in the Linux head build but was not executed. WPF cross-target build result is recorded below; Windows/WPF runtime behavior and side-by-side comparison cannot run here. No render-all or P122 wired claim is made.
- Worker retries: an optional-parameter delegate compile error was fixed; a transient MSBuild child exit was retried serially; disk exhaustion during native dependency copies was resolved by removing only generated output from completed build-only test projects and building the remaining projects sequentially. All final Linux builds passed.

| Build target | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 errors |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` (cross-target) | 0 | 0 errors |

WPF cross-target command: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same serial/copy flags): **exit 0, 512 existing warnings, 0 errors**, including the WPF application and test project. Compiled only; not run on Linux.

## WPF gap inventory and decisions

The row and `MainWindow.QuestsTab.cs` were read at the base above, together with `MainWindow.Quests.cs`, `SkillTreeService.CheckPerfectWeekBonus`, `AchievementService`, `AchievementProgressStreak`, `KeywordTriggerService`, and startup's remote/login hooks.

Completed here: the perfect-week provider (including notification), completed-session credit, received-command credit and the startup call that feeds an existing login streak into `streak_keeper_w`. Existing board/rerolls, season title, art, stats, calendar, shield/program seams and manual streak fixes remain in their existing implementations.

Remaining live gaps:

- Brain-drain minutes require the unported BrainDrainService audio/overlay producer; the Studio control explicitly has no service. Counting an enabled checkbox would falsely award time.
- Keyword credit requires the unported keyword/OCR dispatch engine. `Platform/KeywordTriggerHead.cs` is a no-op demo stub; WPF credits each real single or merged dispatch.
- Issued-command credit requires the controller/subject-directory command producer. Core RemoteRelay is the receiving client and has no CommandIssued event; do not substitute emotes or received commands.
- Startup only feeds the persisted login count. The WPF login-streak lifecycle (cloud-deferred gap resolution, shield/insurance, bonus and rollover) remains head-only in `Models/AchievementProgressStreak.cs`; inventing a simpler increment would change reward and charge behavior.
- A manual streak-fix 401 still lacks WPF ProfileSync unauthorized recovery; this head's SyncPush explicitly omits 401 recovery. Preserve the failed-spend result rather than silently signing out or inventing recovery.
- Intake punch-card accrual/service remains unported. Its UI is deliberately disabled in WPF: at base `e3593f618bd7ccb608761cdd18c580bf44e22a03`, `rg -n 'UiEnabled' ConditioningControlPanel/Services/Progression/IntakePunchCardService.cs` finds `public static readonly bool UiEnabled = false`, and `MainWindow.QuestsTab.cs:392` returns before painting. Avalonia's PunchCardPanel is already hidden. This is **not** a P122 whole-row promotion: other live gaps remain, so no render-all-based wired claim is made.

Related ledger units continue to own the flash voice line, off-tab shell event burst and header stamps (`shell-quests`, `shell-event-fx`, `shell-quest-stamps`). Decoded quest-art cache entries are still dropped on invalidation without explicit disposal while Images may retain them; this is an existing lifetime limitation, not missing WPF UI behavior.

## Assumptions

- Preserve the current head's service boundaries and leave unavailable producers explicit; importing whole keyword, brain-drain, controller and account-recovery features would exceed one unit and the added-line cap.
- Follow WPF's milestone popup policy, including its lack of a perk-notification opt-out. Panic closes this newly reachable popup and cancels queued presentation.
