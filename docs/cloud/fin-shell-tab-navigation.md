# fin-shell-tab-navigation

- Added the remaining WPF tab choreography: 12 px directional slide, capped card stagger, and 100 ms outgoing fade. The outgoing tab is captured before hiding; its image fades without restarting timers, browser hosts or subscriptions on the real page. Rapid navigation and close settle every owned transform/opacity and dispose the image.
- Linked the unchanged pure WPF `Services/ChromeFxNav.cs` into the Avalonia head to share rail order, aliases and browser-airspace exceptions. No frozen WPF file or Core dependency changed.
- Adapted the Intake Pass tile from read-only `parity/trunk` **a4ee52d40cd347d288dcf7fdaa8445b79f8e5038**, merging entry/startup hooks into this base's existing shell. Reused Core's entitlement/niche decisions and the head's shared BeatLoop for the 8 s hold/five half-turn sequence and 1900 ms CTA breath. Removed unused state-copy branches and the imported standalone tween; nothing animates a Transform through Animation.RunAsync.
- Restored WPF's ShowTab-to-EMI usage bookkeeping over the existing Core score store. The EMI nudge listener still depends on its separate moment-bus port.
- Added focused regressions and an explicit entrance-settle seam for the existing pointer tests. No assertions were removed or weakened. No new window, popup or independently registered panic surface was added: the outgoing image is a non-interactive child of the existing shell, and the pass uses its existing click/help surfaces.

## WPF gap audit

Reference: **848964f76513bc90d9a1115463f4a3747d370afe**, frozen WPF TabNavigation.cs, ChromeFx.cs:204-460, Services/MotionFx.cs:105-140, UiUpdates.cs:1300-1925, and MainWindow.xaml.cs:3795-3830.

The base lacked Full entrance slide/stagger, outgoing fade, Home's Intake Pass refresh/tile lifecycle, and EMI tab-open scoring. Those user paths are implemented here. First-visit Deeper/Programs header pulses, Programs pulse stop, Lockdown pulse stop/start, aliases/history and live tab refreshes already existed. StopSeasonTitleShimmer/StopSkillTreeAnimations/StopExclusivesMotion are owned by each view's visibility teardown; UpdatePatreonUI's tab half is the existing RefreshEntitlementVeils.

Remaining dependencies keep the single ledger row **stub**:

- WPF TabNavigation.cs:340-341/:622: AvailableSubjectsService.StopPolling and LeaveLobbyTab/EnterLobbyTab require the unported directory/lobby lifecycle (`views-tab-available-subjects`). The head still renders a sample roster and owns no poller to stop.
- RefreshDashboardRail (WPF FavoritesRail.cs:107) includes RefreshMosaicTierBadges and RefreshDashboardToggleHint. Favorites refresh is wired; tier badges/mystery tile and the toggle-caption counter remain in `shell-presets`.
- EMI TargetOpened's listener needs the unported EMI moment bus. Ring usage itself now works from any tab route.

Dead/obsolete historical gaps, pinned at the reference SHA above:

```sh
git grep -n -E 'BtnPresets_Click|BtnDeeper_Click' 848964f76513bc90d9a1115463f4a3747d370afe -- ConditioningControlPanel
# Definitions and stale documentation comments only; no XAML wiring or live caller.
git grep -n -E 'SetExpandedDoor|IsDoorPanelOpenFor|ApplyNavRailDoorState|ExpandNavRail|CollapseNavRail' 848964f76513bc90d9a1115463f4a3747d370afe -- ConditioningControlPanel/MainWindow
# No matches. The section rail replaced the old hover-open accordion.
```

The historical desktop hover obligation describes deleted WPF behavior. Current rail hover only paints/nudges the fixed section row (NavRail.cs:194); no accordion opens. This is not a P122 wired claim: live dependencies above still remain.

## Assumptions

- Follow the pinned current WPF section rail, rather than recreating its removed door accordion.
- A bitmap fade preserves outgoing appearance while allowing the head's visibility-based service teardown to run once.
- Reduced/Off display an available Intake Pass without ambient turning/breathing, consistent with this head's motion gate.

## Verification limits

SDK: .NET **10.0.100**. The channel install lookup returned HTTP 403; the pinned 10.0.100 installer succeeded. All tests used `DiffEngine_Disabled=true` and existing isolated-profile setup.

All required builds passed with **0 errors**, including the conditional WPF cross-build. The final incremental pass used `dotnet build <project> -c Release --no-restore`; initial builds restored successfully.

| Build target | Exit | Errors |
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
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | 0 |

WPF used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Both the WPF app dependency and its tests compiled; neither ran on Linux.

- `bash .github/scripts/core-guards.sh`: exit 0, **All Core guards passed**. This script needs temporary-file writes; the read-only invocation was rerun with that permission.
- Pre-fix `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~TabTransitionsTests`: exit 1, **4 failed / 3 passed**. EMI's added regression separately failed before its fix (missing sessions usage entry).
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~TabTransitionsTests|FullyQualifiedName~AssetsTabTests|FullyQualifiedName~CompanionPersonalityZoneTests|FullyQualifiedName~NavigationEntryParityTests|FullyQualifiedName~EmiRingWiringTests|FullyQualifiedName~AnimationOnTransformTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~NavSectionRailTests'`: exit 0, **39 passed / 0 failed / 0 skipped**.
- `dotnet run --no-build --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --render-all <temporary-output>` with a fresh `CCP_USERDATA_DIR`: exit 0, **224/224 views rendered**. Inspected the rendered shell. Output stayed outside the branch.
- `git diff --check` and the added private-path scan passed. Only the task branch is pushed; no PR is opened.

Core test results: `dotnet test Tests/CCP.Core.Tests -c Release` returned exit 1 (**2810 passed / 1 failed / 5 skipped**) on the unmodified BoardTiles frame-budget assertion (12.18 ms against 12 ms) while other builds ran. A full `--no-build` retry returned exit 1 with the same counts, this time on the unmodified GoonClockAndRounds scheduled-fire handle assertion. Both classes then passed together: `--filter 'FullyQualifiedName~GoonClockAndRoundsTests|FullyQualifiedName~BoardTilesTests'`, exit 0, **79/79 passed**. No Core implementation or test was changed.

Serialized full Core run: `dotnet test Tests/CCP.Core.Tests -c Release --no-build -- xUnit.ParallelizeTestCollections=false`, exit 0, **2811 passed / 0 failed / 5 skipped**, 51 s. All test assertions remained unchanged; only the invocation serialized collections. The default parallel suite therefore still has order/timing instability to investigate separately.

The five existing Vosk skips need the external speech model, unavailable in this runtime (download blocked). No test skip or weakening was added. Only the selected Avalonia classes ran; the other Linux workflow test assemblies compiled. Windows-specific execution and real-desktop interaction remain untested. The row remains stub for the dependencies listed above.
