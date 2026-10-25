# fin4: shell-tab-fx-presets-quests-achievements

- Dependency-only audit; no application, test, or ledger change. The row remains `stub`.
- Required base verified: `39ef05790a00b6613e600c657552b71b722269c6`; clean checkout on local `work`, then the requested unit branch.
- Read-only `git fetch origin parity/trunk` resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; existing base implementations win.
- Dependency: `shell-takeaway` (live device-token receipt drawer, complete receipt UI/actions, and replay-host integration). `shell-chrome-fx` will also require incoming-tab filtering when it introduces outgoing fade re-shows.

## Pinned WPF behavior audit

All references below are at base commit `39ef05790a00b6613e600c657552b71b722269c6`. The specification is `ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs` (833 newline-terminated lines). Compared against the actual view implementations, not just the shell placeholder.

| WPF behavior | Existing Avalonia implementation / remaining gap |
| --- | --- |
| One selected preset/session sheen, rebuilt on selection and mod change; active, minimized, visible, motion/performance gates; detach on hide | `PresetsTabView.Fx.cs`, `CardSheenAdorner`; already implemented. |
| Preset chip hover 1.02 / 150 ms; session row rise -2 / 150 ms without scale; preserve authored transforms | `PresetsTabView.Fx.cs`; already implemented for existing cards/rows. Receipt and door chips have no live builders yet. |
| One session-row sweep, enter/leave, park idle, detach when rebuilding/hiding/mod switching | `PresetsTabView.axaml.cs:953-991` and focal FX teardown; already implemented. |
| Primary-button press 0.97 / 80 ms, release/leave reset; no duplicate squish on quest reroll buttons | Focal FX `WirePress`, existing quest button templates; already implemented. |
| On arrival: sync unseen custom session files, repaint/re-sort rack, refresh receipts, then independent entrance zones (40 ms stagger, six-slot cap, opacity 220 ms, rise 260 ms) | Focal FX arrival funnel already syncs/repaints rack and animates existing zones. `SeedTakeaway` only clears the receipt shelf: **missing live receipt refresh and receipt/door chip hover/entrance FX**. |
| Only incoming tab runs arrival work when outgoing tab is temporarily re-shown for fade | **Deferred dependency**: `MainShellWindow.TabNavigation.cs:ShowTab` directly assigns visibility, with no outgoing fade/re-show. `MainShellWindow.ChromeFx.cs` explicitly lists that choreography as unported. No current event to filter. |
| Weekly fraction clamped, cached before measurement, 600 ms fill transition, immediate resize seating, unchanged width threshold, Off snaps | `QuestsTabView.Quests.cs:ApplyWeeklyBar`; already implemented. Daily cards own their progress; no second quest ambient loop. |
| Free achievement grid only, visible tiles stagger; unlocked badge lift/side tilt 0.8 degrees / 160 ms and independent badge pop; relocking resets tilt; no achievement ambient loop | `AchievementsTabView.Fx.cs` and tile rebuild/unlock handling; already implemented. Unlock particles/nav-anchor fallback belongs to `MainWindow.EventFx.cs` / `shell-event-fx`, not the cited focal FX file. |

Existing focused coverage: `PresetsFocalFxTests` (five tests), `AchievementsTabFxTests`, `PresetsTakeawayEmptyStateTests`, plus `GateFxAdornerTests` row-sweep coverage. No new behavior or surface is added, so no new test-plan row or panic registration is needed.

## Why the remaining dependency is not a small FX patch

`MainWindow.Takeaway.cs` is 670 lines, `JustDropOrdersService.cs` is 189, and `JustDropHostService.cs` is 301 (1,160 total reference lines). These are a scope indicator, not a claim that source line count equals adapted line count. The existing shop host is reusable, as are Core availability, `JustDropHandoff`, account recovery, and the credential seam. Their presence does not provide the following missing paths:

1. Server order DTO/parser, unknown-size behavior, signed-out/failure empty result, cancellation, serialized reads, 90-second cache and explicit invalidation; authenticated GET using the existing credential seam, with merged-account recovery. No persisted/local order list.
2. Asynchronous shelf loading without blanking the previous drawer, concurrent-load guard, repaint on arrival and host close. Newest three pinned, tray containing **all** orders, overflow count/toggle reset on repaint, localized minutes/date/age/fallback name, count/empty/door gating.
3. Receipt replay, nested copy-link click suppression and four-second success/failure toast, door recheck on click, receipt/door chip hover and arrival seating. Existing unused ControlThemes can be reused; new icons must use Fluent assets.
4. Replay-specific bare-player launch, fullscreen/host strip/Esc, panel duck and restore, single-host refocus/lifecycle, close-time invalidate-before-refresh. `GameWindow.JustDrop.cs` expressly excludes replay fullscreen/duck; its current `LoadJustDrop` always targets the shop. `JustDropHandoff.ReplayPath` alone is not a caller.
5. Focused Core tests for fetch/cache/parser/recovery and headless tests for the shelf/actions/replay lifecycle, sandboxed against desktop/profile side effects, plus ledger/test-plan documentation and panic lifecycle proof.

A faithful adapted implementation of that complete feature and its behavior tests is estimated above the 1,000-added-line limit; merely introducing a DTO or fake shelf would leave this row stub and ship unused code. Per the unit's explicit pure-dependency exception, this branch therefore makes **no code change** and records the dependency instead of partially porting another unit. This is an estimate of complete scope, not an experimentally established minimum.

## P122 caller proof

This is **not** dead WPF behavior and cannot be promoted to wired under P122. Reproduce at the pinned SHA:

```sh
git grep -n 'RefreshTakeawayShelf' 39ef05790a00b6613e600c657552b71b722269c6 -- ConditioningControlPanel
git grep -n 'PreparePresetCardFx(chip)' 39ef05790a00b6613e600c657552b71b722269c6 -- ConditioningControlPanel/MainWindow/MainWindow.Takeaway.cs
git grep -n 'OnPresetsTabVisibilityChanged' 39ef05790a00b6613e600c657552b71b722269c6 -- ConditioningControlPanel/Views/Tabs/PresetsTabView.xaml.cs
```

The arrival handler calls `RefreshTakeawayShelf` at line 286; Takeaway receipt and door builders call `PreparePresetCardFx` at lines 244 and 499. WPF host disposal invalidates the drawer before refreshing it. The head has no `JustDropOrdersService` implementation in Core and no receipt builders; its shell visibility fan-out only logs. Render-all cannot prove a live drawer from an empty shelf.

## Verification limits

SDK: .NET 10.0.402, installed with the requested installer (latest-version probe returned 403 on its fallback endpoint; SDK download/install succeeded). All test invocations set `DiffEngine_Disabled=true`. No Windows execution, real account request, clipboard, or desktop notification is attempted. No credential or secret file was inspected.

### Build commands

Each command was `dotnet build <project> -c Release --nologo`. All 13 Linux project builds exited 0 with `0 Error(s)`:

- `CCP.Core/CCP.Core.csproj` (48 warnings)
- `CCP.Avalonia/CCP.Avalonia.csproj` (172 warnings)
- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` (1 warning)
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` (10 warnings)
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`

WPF cross-target build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings / 0 errors. Frozen WPF app and tests compiled, not run.

### Behavior checks

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed` (424 XAML references; 1,591 app types / 1,880 Core types). Initial read-only sandbox invocation could not create the script's temporary files and exited 1; rerun with write access passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --nologo`: exit 1, 2,810 passed / 1 failed / 5 skipped (optional Vosk model absent). Existing `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` failed its non-null scheduled-handle assertion. No source/test changes were made.
- Isolated Core retry, same project with `--no-build --filter FullyQualifiedName~GoonClockAndRoundsTests`: exit 0, 12 passed / 0 failed.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~PresetsFocalFxTests|FullyQualifiedName~AchievementsTabFxTests|FullyQualifiedName~PresetsTakeawayEmptyStateTests|FullyQualifiedName~GateFxAdornerTests' --nologo`: exit 1, 12 passed / 1 failed. `ArrivalSyncsDraftsAndRecentOrderBeforeTheStagger` passed its rack ordering and disk sync assertions, then failed at line 182 expecting a card still at opacity < 1 after layout/render work. This was not a font-creation failure.
- Isolated headless retry, same project with `--no-build --filter FullyQualifiedName~ArrivalSyncsDraftsAndRecentOrderBeforeTheStagger`: exit 0, 1 passed / 0 failed. The initial mixed run remains reported; no tests were weakened or skipped.
- Test-plan coverage script is absent on this base; no ledger row was added/promoted. Render-all was not run: no render change or P122 promotion is claimed.

- Full Core rerun with `--no-build`: exit 1, 2,809 passed / 2 failed / 5 skipped. The scheduled-handle assertion recurred, and `BoardTilesTests.A_frame_fits_the_budget` measured 16.76 ms. These unchanged baseline failures remain unresolved; the full suite is not claimed green.

## Assumptions

- The explicit dependency-only NOOP rule takes precedence over adding a partial receipt feature. The ledger's existing Missing list remains honest and unchanged.
- Incoming-tab filtering becomes due with actual outgoing fade re-shows; introducing it now would require inventing shell choreography outside this unit.
