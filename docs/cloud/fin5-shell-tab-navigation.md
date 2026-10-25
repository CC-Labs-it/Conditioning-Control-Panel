- Base verified at `848964f76513bc90d9a1115463f4a3747d370afe`; cloud branch name was `work`.
- Complete finite Full-motion tab entrance: WPF directional 12 px / 200 ms slide, 220 ms card fade, 260 ms rise, 40 ms stagger capped at six delay slots, up to eight eligible cards. Reduced keeps the crossfade; Off snaps; browser airspace pages do not slide.
- Add an explicit entrance-settle seam for gesture tests, with cancellation and authored-transform restoration on navigation and shell close.
- Adapt WPF's 100 ms outgoing fade to a noninteractive bitmap sibling. The actual page stays hidden so its visibility-driven timers and services never restart for the fade. Snapshot resources are removed on completion, interruption, or close.
- Shared navigation direction decisions are adapted from the frozen WPF reference at the base SHA, not imported from trunk. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.
- Keep `shell-tab-navigation` stub: the remaining dependencies below do not have live Avalonia hosts.

## WPF gap audit

Compared the cited `MainWindow.TabNavigation.cs`, `MainWindow.ChromeFx.cs` (AnimateTabIn, SlideTabIn, FadeOutgoingTab, StaggerTabCards), `Services/ChromeFxNav.cs`, `Services/MotionFx.cs`, `MainWindow.UiUpdates.cs` (RefreshIntakePassTile), and `MainWindow.Lobby.cs` at the pinned base.

Before this unit, the gaps were Full-motion slide/card stagger, outgoing fade, Intake Pass tile refresh/ceremony on settings entry, Available Subjects polling/lobby teardown, and recorded desktop rail hover validation. Earlier row notes about first-visit headers, Lockdown/Programs pulse stops, entitlement repaint and shimmer teardown are already implemented on this base. Presets/Deeper button handlers have no live WPF XAML callers, as recorded on the base; no new P122 promotion is claimed.

Remaining missing behavior:
- Intake Pass Dashboard host: `RefreshIntakePassTile` also owns weekly reveal/spin/alternation, per-mod art, pass-state and mod events, CTA pulse and visibility cancellation. This is not a single refresh call: the Avalonia Dashboard has markup but no ceremony host. The surrounding WPF implementation is hundreds of lines of transforms/timing; porting a partial refresh would misrepresent parity.
- Available Subjects host: WPF ShowTab calls `StopPolling` and `LeaveLobbyTab` on every accepted navigation, then starts the lobby on its case. Avalonia still has sample roster data and inert claim/connect handlers. The WPF directory service alone is 395 lines and lobby partial 293, before transport/UI adaptation and tests. A lifecycle-only method without a real polling/claim/join host would be a no-op. This requires the Available Subjects/lobby port and exceeds this unit's remaining 1000-added-line budget.
- Dashboard rail entry refresh: favorites are wired, but the WPF `RefreshDashboardRail` also calls `RefreshMosaicTierBadges` and `RefreshDashboardToggleHint`. Those Dashboard presentation hosts remain unported (the base Presets partial explicitly names them).
- EMI tab-open accounting: WPF `EmiTargets.NoteTabOpened` forwards usage and `TargetOpened` through its desk service. Avalonia has a summon/dismiss service but lacks that notification/moment bus; this older omission is now explicitly retained as a dependency rather than hidden in a source header.
- Core already supplies `IntakePassService` and chess `PbpLobbyApi`/`LobbyModel`; neither supplies the Dashboard ceremony or the live Available Subjects lease/poll host. Those were checked before deferring the hosts.
- Real desktop rail hover validation remains owed. Headless gesture checks cannot establish native desktop hover behavior.

## Verification limits

Validation used .NET SDK `10.0.402` and `DiffEngine_Disabled=true`.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build <project> -c Release` for every Linux test project in the workflow: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, main Avalonia, Core.Content, Core.Settings and Core.Tests: all eleven exit 0, zero errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (512 existing warnings). WPF application and tests compiled, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The test-plan coverage script is absent on this base; numbered tester steps were supplied anyway. `git diff --cached --check`: exit 0; one ledger row remains for the id.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 2,823 passed, 5 skipped, 0 failed (2,828 total). Includes all 12 new pure navigation cases. Vosk skips reflect the unavailable model; no tests were changed or disabled. An earlier run during builds failed the existing Board frame-time budget (12.90 ms); the sequential final run passed unchanged.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~TabTransitionsTests|FullyQualifiedName~AssetsTabTests|FullyQualifiedName~CompanionPersonalityZoneTests|FullyQualifiedName~NavDoorRailGateTests'`: exit 1; 13 font-creation failures during shell construction, one standalone test passed. The error is `Could not create glyphTypeface`, family `Segoe UI Black`, in the existing PremiumSpark constructor. New shell assertions therefore remain unexecuted; supervisor CI is required. An initial baseline attempt failed to compile against an outdated test-seam reference and supplies no behavioral failure proof.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~StaggerTraversalSkipsHiddenAndDimmedCardsAndCapsAtEight`: exit 0; 1 passed.
- Desktop rail hover, render-all and Windows runtime behavior were not run. No new parity verification is claimed.

 Windows-specific code can be compiled here but cannot be executed on Linux. No real desktop, real profile, browser launch, or live lobby/account request is used by the tests. No new window or notification surface is added; the existing shell panic registration covers the changes.

## Assumptions

- Preserve WPF browser-airspace exemptions even where a platform could animate the browser.
- A bitmap outgoing fade is the smallest safe Avalonia adaptation: it preserves appearance while respecting this head's visibility-based lifecycle.
- Keep the row `stub` until the named service hosts and release hover check are complete.
