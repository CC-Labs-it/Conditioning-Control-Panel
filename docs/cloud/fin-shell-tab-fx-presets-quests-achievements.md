# fin-shell-tab-fx-presets-quests-achievements

- Audited the final `shell-tab-fx-presets-quests-achievements` ledger row against frozen WPF at `97a59efe8561f3e6b09c74fd4028b235ec005d4c`.
- Dependency-only NOOP: no product or test code changed, no new surface, and no new panic registration needed. Keep the single ledger row `stub` with explicit remaining dependencies.
- No trunk code imported. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; there are no imported commits to credit.

## WPF behavior audit

All file references below are relative to the repository and pinned to the base SHA above.

| WPF behavior | Existing Avalonia user path / remaining dependency |
| --- | --- |
| `OnPresetsTabVisibilityChanged`: sync unseen custom-session files, repaint Recent ordering, refresh shelf, then initialize/stagger/sheen (WPF TabFx file:252-293) | `PresetsTabView.Fx.cs:81-99` syncs/repaints before entrance; `SyncCustomSessionsFromDisk` in the view re-points selection and rebuilds toolbar counts without `SessionAdded`. Receipt refresh is blocked by `shell-takeaway`, below. |
| Selected preset/session sheen; preset selection takes precedence; radius 17/10; detach on hide and park for inactive/minimized window or ambient gate; rebuild tint on mod change (TabFx:124-184,517-594) | `PresetsTabView.Fx.cs` hooks window, motion and mod changes; selection/build callers refresh one `CardSheenAdorner`. The adorner uses the shared clock. |
| Preset chip 1.02 hover, session row -2 px rise without scale, primary buttons 0.97 squish (TabFx:218-245,295-369,430-453) | View builders wire chip/row events and primary-button press/release/leave/capture-loss; transitions snap at Off. Receipt/shop chips cannot receive their hover until the Takeaway builders exist. |
| One hovered-row sweep, re-enter/reuse, leave to idle, detach on row rebuild/tab hide/mod switch (TabFx:371-428) | `PresetsTabView.axaml.cs:953-988` plus focal-FX hide/mod/unload hooks; existing `GateFxAdornerTests.SessionRowSweepsInOnHoverAndOutOnLeave`. |
| Independent preset, session and Takeaway entrance zones; visible Borders only; no replay on selection; 40 ms spacing capped at six slots, 220 ms fade/260 ms rise (TabFx:459-512; MotionFx.StaggerIn) | `StaggerZones` and selection builders implement the live zones; Takeaway has no receipt Borders to stagger until `shell-takeaway` supplies them. |
| Weekly fraction storage, clamping, first-layout seating, 0.6 s QuadraticEaseOut fill, immediate resize/Off; no second quest ambient hero or compounded reroll squish (TabFx:599-689) | `QuestsTabView.Quests.cs` refreshes the real weekly fraction and `ApplyWeeklyBar`, with track-size handling; daily bars remain owned by their daily cards as in WPF. |
| Achievements: free-grid-only entrance; unlocked badge-host 1.02 lift and pointer-side +/-0.8 degree tilt over 160 ms; independent badge pop and reset; locked cards remain calm; no new ambient loop (TabFx:694-830) | `AchievementsTabView.Fx.cs` and its constructor/grid wiring already implement these user paths; existing `AchievementsTabFxTests` covers real navigation/hover and motion gates. |
| Ignore outgoing-tab visibility re-shows for all three tabs via `IsIncomingTab` (TabFx:127-136,268,603,698) | Deferred to `shell-chrome-fx`: `MainShellWindow.TabNavigation.cs:140-193` currently sets visibility directly, with no `FadeOutgoingTab` re-show. Adding a guard now would filter no live event. |

## Remaining dependencies (complete list for this row)

1. **`shell-takeaway`: live receipt data and shelf builders.** WPF TabFx:286 calls `RefreshTakeawayShelf`, whose live fetch/repaint path is WPF `MainWindow.Takeaway.cs:77-189`. `JustDropOrdersService.Order` and `FetchAsync` exist only under the WPF head; there is no order-drawer service/type in Core. The WPF service reads newest-first receipts with a 90-second cache, serializes concurrent fetches and invalidates on shop/replay close. Avalonia currently runs `SeedTakeaway`, the honest empty state, before the entrance.
   - When that dependency lands, preserve existing receipts while an async refresh is in flight, prevent overlapping loads, and repaint safely on completion/failure.
   - Real rows require the newest-three pinned chips, all-orders overflow tray, `+n more` count/toggle, collapse-on-repaint, formatted receipt metadata and empty/count/door visibility. Their hover hooks and entrance zone must then operate on the actual receipt/door chips.
   - The owning Takeaway feature also supplies copy-link/toast, shop-door refresh fan-out and close invalidation, plus replay. WPF replay fullscreen/window duck is still absent from `GameWindow.JustDrop.cs:16`.
   - Do not inherit stale blocker claims from `MainShellWindow.Takeaway.cs`: Core secrets, `JustDropService.DoorAvailable`, `JustDropHandoff` and the windowed shop host already exist on this base. The order drawer and replay/shelf integration are the missing pieces; absence of all Just Drop infrastructure is not the reason.
2. **`shell-chrome-fx`: outgoing/incoming tab choreography.** Port `IsIncomingTab` alongside the transition's pending-tab key and outgoing fade re-show. Current snap navigation never generates the false arrival this guard prevents.

Neither dependency is dead WPF code: the arrival funnel calls the shelf refresh, the Takeaway builders call `PreparePresetCardFx`, and WPF `FadeOutgoingTab` re-shows the outgoing element. P122 does not apply and this row must not become `wired` or `verified`.

Reproduce the source checks at the pinned base:

```bash
git grep -n 'RefreshTakeawayShelf\|IsIncomingTab' 97a59efe8561f3e6b09c74fd4028b235ec005d4c -- ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs
git grep -n 'PreparePresetCardFx' 97a59efe8561f3e6b09c74fd4028b235ec005d4c -- ConditioningControlPanel/MainWindow/MainWindow.Takeaway.cs
git grep -n 'class JustDropOrdersService' 97a59efe8561f3e6b09c74fd4028b235ec005d4c -- CCP.Core ConditioningControlPanel/Services/JustDrop
git grep -n 'FadeOutgoingTab\|Visibility = Visibility.Visible' 97a59efe8561f3e6b09c74fd4028b235ec005d4c -- ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs
git show 97a59efe8561f3e6b09c74fd4028b235ec005d4c:CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs
```

## Verification limits

SDK: .NET `10.0.402`. Builds used Release configuration. Test execution used `DiffEngine_Disabled=true`.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 48 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 172 warnings, 0 errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0; 118 warnings, 0 errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0; 10 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0; 50 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0 on retry; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0 on retry; 1 warning, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0 on retry; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0 on retry; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0 on retry; 0 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0 on retry; 0 warnings, 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 on retry; 512 warnings, 0 errors; WPF app and tests compiled, not run |
| `bash .github/scripts/core-guards.sh` | 0 on retry; All Core guards passed; 424 XAML references checked |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 0 on retry; Failed 0, Passed 2811, Skipped 5, Total 2816, Duration 49 s |
| Pinned source checks above; `git diff --check` | 0; expected live callers found; no whitespace errors |

Infrastructure recovery: the initial checkout fetch exited 255 (read-only Git metadata); the authorized fresh-clone fallback verified the exact expected base and clean status. A temporary environment disconnect recovered without losing the clone. The first build batch exhausted disk space while copying native runtime assets into multiple test outputs: later builds/test startup exited 134 and the initial guard invocation exited 1. Removing generated test output folders between builds allowed every affected check to complete successfully, with no source or project changes.

Not run: headless `PresetsFocalFxTests`, `GateFxAdornerTests`, `AchievementsTabFxTests` and `PresetsTakeawayEmptyStateTests` under `Tests/CCP.Avalonia.Tests`, per the task's known cloud font-creation limitation. The main test project compiled with zero errors; runtime validation is left to the supervisor's GitHub rcheck. No tests were changed or disabled. The five optional Vosk tests skipped because the model is unavailable; no blocked download was attempted. Windows desktop behavior was cross-compiled only. Render-all and a real desktop session were not run; P122 is not being claimed.

## Assumptions

- Treat receipt refresh and receipt-chip FX as dependent on the owning `shell-takeaway` feature, rather than introducing a second order model or a partial receipt UI within this FX unit.
- Keep snap navigation until `shell-chrome-fx` owns the transition; the incoming-tab guard becomes necessary with that transition.
