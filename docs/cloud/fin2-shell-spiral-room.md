# fin2-shell-spiral-room

- Dependency-only NOOP: retain `shell-spiral-room` as `stub`; no runtime code, tests, or ledger claims changed.
- Audited the last (and only) row against WPF at base commit `7fc121d2fa986c987eb80b701b395996399b696e`.
- Read-only trunk inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported, so no imported commits require credit. Trunk has a Core `DescentService`, but importing that service alone does not supply this head's missing account lifecycle, block refresh callers, or live migration owner.

## Behaviour audit

All paths below refer to the pinned base commit above.

| WPF behaviour | Avalonia state / remaining dependency |
| --- | --- |
| `MainWindow/MainWindow.SpiralRoom.cs`: idempotent initializer subscribes to phase, block and language changes. Its refresh method is now empty (lines 74–80): the old rail row is retired, the You pill stays hidden and the fuse chip is the fog entry. | Fuse entry and phase updates already work (`SpiralRoomShellTests`). Reproducing subscriptions to an empty refresh adds no behaviour. Do not resurrect the old row or invent a fog pill. |
| The tab is entered through `ShowTab("spiral")`, which refreshes its current gates rather than trusting the last paint. | Already wired to `OnTabShown`; persisted migration offers, valid pending answers, acknowledgement, cloud settings replacement, and hide/detach unsubscription are covered by `SpiralMigrationGateTests` and shared `SpiralWithholdRulesTests`. |
| `DescentMigrationService.SpiralWithheld` includes an in-memory offer as well as saved gates. Holds retain that offer; release applies it. Gate changes re-raise `DescentService.BlockChanged`. An acknowledged migration or valid pending answer wins. | Shared predicate and persisted observations exist; live offer ownership and its notifications do not. Requires the unported **DescentMigrationService offer/apply/hold lifecycle**, including the profile-sync offer caller. The arithmetic alone is not a live owner. |
| Ceremony-open input to the withhold. | No gap in live behaviour: WPF `DescentMigrationService.cs:66` returns `false` permanently after ceremony retirement. Avalonia's `false` matches. Do not port a retired ceremony window. |
| `SpiralTabView.xaml.cs:307–335,414–420`: block arrival/update posts current state to an existing embed and reselects the room; withdrawal reselects Waiting. Fuse fog still outranks a block. Hidden views stop observing. | `Refresh` deliberately passes `hasBlock: false`; no head/Core `DescentService` instance supplies `Current` or `BlockChanged`. Requires the unported **DescentService account-scoped fetch/reset/refresh lifecycle and shell ownership**. Existing Core state arithmetic and embed protocol cannot fabricate a server block. |
| The visible Spiral state constructs the embed, supplies its initial block, observes later blocks and tears it down on leaving, preserving waiting/failure handling. | Embed protocol and view exist, but no production block producer can reach this state. Same DescentService dependency; a fake block or permanent `hasBlock: true` would misrepresent server authority. |
| `BeginSpiralFirstLight` brings entry through ShowTab **before** BeginFirstLight; director restores/activates the main window first. | No Avalonia show-director caller or shell landing method. WPF `DescentShowDirector.cs:423–425` explicitly says the scratch rig is now its only caller. Repository-wide source search finds the definition and director-to-shell call, but no production call to `RunFirstLightReveal`. This is retired production behaviour, not a reason to add a new user path. |
| First Light, if invoked by a rig, owns the surface: tears down embed, hides copy/help/splash/ambience, snapshots reduced motion (0.8-second crossfade versus the full shared timeline), runs the visual timeline, waits for the first non-null block, tolerates the initial null notifications, and returns to normal selection on completion/failure or a 12-second block timeout. | Avalonia BeginFirstLight soft-falls back to Refresh. Requires **SpiralFirstLightVisual** and the block lifecycle, and has no live WPF production caller. Core timeline arithmetic already exists. No P122 `wired` claim: other live dependencies remain, and render-all proof was not established. |

The companion's once-ever Spiral entry bark and room sound decoration in the WPF tab also depend on the unported **DescentBarkWatcher / DescentRoomSfx** services. They are downstream room effects rather than missing navigation. They must be considered when those service/room units land; no unused shell adapter is added here.

## Reproducible evidence

At the pinned base SHA, run:

```sh
rg -n 'RunFirstLightReveal|BeginSpiralFirstLight' ConditioningControlPanel --glob '*.cs'
rg -n 'OfferReceived\(' ConditioningControlPanel --glob '*.cs'
rg -n 'IsCeremonyOpen|LiveOffer|NotifySpiralSurfaces' ConditioningControlPanel/Services/Descent/DescentMigrationService.cs
rg -n 'hasBlock: false|SpiralWithheldFor|OnTabShown|BlockChanged' CCP.Avalonia/Views/Tabs/SpiralTabView.axaml.cs
rg -n 'class (DescentService|DescentMigrationService|DescentShowDirector)' CCP.Core CCP.Avalonia --glob '*.cs'
```

The last command finds no service implementations. WPF's production offer caller is `Services/Settings/ProfileSyncService.cs:4945`; the First Light search finds only its director definition, the director's shell call, and the shell method. Comments describing the old ceremony are not live callers.

## Verification limits

SDK: .NET `10.0.402`, Linux. `DiffEngine_Disabled=true` for test runs; isolated `CCP_USERDATA_DIR` roots. No runtime behaviour changed, so no new surface/panic registration, new behavioural test, or fix fail-before demonstration is applicable.

| Command | Result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | Exit 0; 0 errors (48 existing warnings). |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | Exit 0; 0 errors (193 existing warnings). |
| `dotnet build Tests/<project>/<project>.csproj -c Release` for every Linux workflow test project listed below | All exit 0; all 0 errors. |
| `bash .github/scripts/core-guards.sh` | Exit 0; both guards pass, 429 XAML references checked. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --no-restore` | Exit 0; 3743 passed, 5 existing Vosk-model skips, 0 failed (34 seconds). No tests modified or disabled. |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~SpiralRoomShellTests\|FullyQualifiedName~SpiralMigrationGateTests'` | Exit 0; all 4 passed, 0 skipped, 0 failed (6 seconds). Font creation succeeded here. |
| `git diff --check` | Exit 0. |

Linux workflow test projects built: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`.

The first batch exhausted temporary disk space while copying repeated native runtime assets. Generated test outputs were removed between subsequent builds; every Linux build was then completed successfully. Source and tests were unchanged during recovery.

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 552 existing warnings, 0 errors; compiled but not run on Linux. Windows execution, native browser integration, render-all, full Avalonia suites, and Vosk-model tests were not run. This dependency audit does not establish rendering or Windows parity.

## Assumptions

- Apply the task's explicit pure-dependency rule instead of importing whole account/migration services into a shell-only unit.
- Current executable WPF behaviour takes precedence over historical ledger prose: ceremony-open is permanently false and the First Light production caller has retired.
- Keep the ledger row unchanged and `stub`; the dependency analysis does not establish complete parity.
