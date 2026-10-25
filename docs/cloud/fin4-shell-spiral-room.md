# fin4-shell-spiral-room — dependency analysis

- No application code or ledger status changed. `shell-spiral-room` remains `stub`.
- Base and WPF reference: `df5a59167c58ab3735c2a5295db3b3b7243af475` (exact expected commit, clean before branching). The initial snapshot was a different commit and read-only; the authorized fallback clone was used.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported; no trunk attribution is needed.
- Prerequisite: **Descent block service and migration offer/submit lifecycle**, including account reset and sync ownership. A synthetic block or test-only setter would not create the required real user path.

## WPF comparison and remaining behaviours

All references below are pinned to the base SHA above.

1. `MainWindow/MainWindow.SpiralRoom.cs:40-76`: initialize once; observe fuse phase, block/withhold and language changes. `RefreshSpiralRailEntry` is intentionally empty after the section-rail rework. Avalonia already has the fuse-chip navigation path; adding another rail row would diverge from WPF.
2. `Views/Tabs/SpiralTabView.xaml.cs:239-331,414-420`: subscribe to the live block while shown, unsubscribe when hidden, marshal accepted reads to the UI thread, recompute the room on block/withhold changes, and send non-null updates to an existing embed. Avalonia observes the fuse and persisted migration settings but hardcodes `hasBlock: false` (`SpiralTabView.axaml.cs:386`) and publishes `PostState(null)` (`:657`). Thus normal server-backed spiral entry, updated embed state and withdrawal back to waiting are unreachable.
3. `Services/Descent/DescentMigrationService.cs`: a live offer contributes to withhold, depth-counted holds preserve the offer, final release silently restores the ledger, a valid pending choice opens the gate immediately, and block notifications refresh surfaces. The ceremony is retired: `IsCeremonyOpen` is always false. Avalonia has the shared persisted predicate and acknowledgement, but no live offer owner or local migration runtime.
4. `Views/Tabs/SpiralTabView.xaml.cs:364`: each room entry notifies the once-ever first-open bark. Avalonia has no `DescentBarkWatcher` notification at `OnTabShown`; WPF's watcher delegates to `App.Bark.NotifyDescentFirstSpiralOpen(Current)` and requires the live block and companion host.
5. `MainWindow/MainWindow.SpiralRoom.cs:96-108`: first-light navigation must precede reveal. WPF's reveal owns the surface, removes native embed airspace/help/ambient layers, captures reduced motion once, waits for a first block without treating null as withdrawal, times out after 12 seconds, and returns to ordinary selection on completion, hide, timeout or error. Avalonia's `BeginFirstLight` only refreshes; its drawing primitive/timeline exists. This is a retained rig seam, not a missing production ceremony trigger (see caller proof below).
6. The tab's motion comments also record the separate OS animation-preference cap dependency (`SpiralTabView.axaml.cs:157`): the shared ambient environment does not cap Full to Reduced from Windows animation settings. This is a platform motion seam, not shell navigation behaviour. Native embed process-loss/pre-document carrier limitations belong to WebHost, not the shell row.

## Why this is a dependency-only unit

`CCP.Core/Services/Account/SyncPush.cs:268-270` explicitly states that this head never takes an offer (no `descent_auto`); it applies only `DescentMigrationAck`. Although `SyncBody` defines the auto field, the missing offer consumer and choice-submit path cannot safely be enabled just for this room. WPF's `ProfileSyncService.cs:1589-1593,1742-1771,1993-2005,2366-2368,2499-2513,4909-4945` specifies the coupled work: opt-in, offer parse/apply, pending-choice payload, protection against pre-migration server figures until ack, stage drip, and post-sync block refresh. `DescentService` also needs sign-in, logout generation protection, refresh coalescing and failure retention; a one-shot profile GET is insufficient.

The three WPF supporting hosts (`DescentService`, `DescentMigrationService`, `DescentBarkWatcher`) total 964 source lines before the sync integration, shell lifecycle and focused behavioural tests. Adapting shared logic to Core reduces comments/platform code, but a correct complete port also adds those missing integration and test paths. This is a service/migration unit exceeding this unit's 1000-added-line budget, not a small shell hook. Following the task's pure-dependency rule, no partial service or render-only promotion was added. No new surface requires panic registration; no new behaviour or ledger promotion requires a tester step.

## First-light caller proof (P122 scope)

At `df5a59167c58ab3735c2a5295db3b3b7243af475`:

```sh
git grep -n -E 'RunFirstLightReveal|BeginSpiralFirstLight' df5a59167c58ab3735c2a5295db3b3b7243af475 -- ConditioningControlPanel
```

Matches are the `RunFirstLightReveal` declaration (`DescentShowDirector.cs:428`), the `BeginSpiralFirstLight` declaration (`MainWindow.SpiralRoom.cs:96`), and the director's comment/call (`DescentShowDirector.cs:467-470`). There is no production caller of `RunFirstLightReveal`. Its preceding documentation explicitly says the capture rig is its only caller after the ceremony's retirement. This removes a need to revive the retired ceremony; it does **not** justify marking the whole row wired while the live block path is missing. No P122 promotion or render-all claim is made.

## Assumptions

- The frozen WPF at the required base is authoritative, including its ceremony retirement; historical ledger mentions of ceremony ownership do not authorize resurrecting it.
- The prerequisite should land as a cohesive migration/service unit before finishing this shell row; persisted gates and test injection alone do not satisfy a real user path.

## Verification limits

- SDK: .NET 10.0.402. The channel install lookup returned HTTP 403; the pinned official SDK install succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release -m:1 -p:UseSharedCompilation=false`: exit 0, 48 warnings, 0 errors.
- Same build for `CCP.Avalonia/CCP.Avalonia.csproj`: exit 0, 193 warnings, 0 errors.
- Same build for every Linux test project listed in `.github/workflows/build.yml`: exit 0, 0 errors each: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests` (under `Tests/`). Main Avalonia tests: 96 warnings. No source was changed to obtain these results.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references, 1605 app types / 2175 Core types). The initial read-only sandbox attempt could not create temporary files; the writable rerun passed.
- `test-plan-coverage.sh` is absent from this base (attempt exit 127); no ledger row changed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0, 4117 passed, 5 existing Vosk tests skipped, 4122 total. Initial run during compilation: exit 1, 4116 passed, one board frame-budget failure (13.10 ms against 12 ms); its focused rerun passed, then the entire suite passed after builds finished. No tests were modified or disabled.
- With the same environment, `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~SpiralRoomShellTests|FullyQualifiedName~SpiralMigrationGateTests'`: exit 0, 4 passed, 0 failed/skipped. Headless font creation worked on this base/runtime; tests used their temporary user-data profiles.
- WPF test-project build attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: restore succeeded after a transient NuGet 503; compilation did not complete (exit 1 during disk exhaustion, incomplete diagnostic log). No successful WPF build or Windows execution is claimed.
- Verification initially oversubscribed the four-CPU runtime and later exhausted its 8.8 GB writable disk with duplicated native test outputs. Builds were rerun with bounded .NET concurrency and generated outputs removed between test projects; all required Linux builds ultimately passed. No dependency or source files were removed.
- `git diff --cached --check`: exit 0. Documentation only; no new behavioural tests were needed.
- Not run: Windows UI/tests, native browser integration, real-account sync/migration, render-all, or unrelated Avalonia test classes. No real desktop, notification or user-profile interaction was used.
