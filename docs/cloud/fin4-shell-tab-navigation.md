# fin4-shell-tab-navigation

- Base checked at `848964f76513bc90d9a1115463f4a3747d370afe`. The supplied snapshot had a different HEAD and read-only Git metadata; used the requested fresh-clone fallback and pinned the expected base before editing.
- Restore WPF Full-motion tab entrances: 12 px directional slide over 200 ms with QuadraticEaseOut, plus top-level card stagger (40 ms delay slots capped at six, eight targets maximum, 220 ms fade and 260 ms rise). Preserve authored transforms, dimmed/hidden cards, Reduced/Off and native-airspace exceptions.
- Adapt the pure `ChromeFxNav` decisions into Core, preserving the frozen WPF source and using Core's existing NavSections order. No trunk imports; reference code is from the pinned base above.
- Fade the outgoing page for 100 ms using a non-interactive frozen frame. This avoids reactivating the old page's visibility-owned refreshes and services. Cancel entrances on subsequent navigation and close; release the bitmap on fade completion, navigation and close.
- Add deterministic `AdvanceTabEntrance` and `SettleTabEntrance` seams for pointer tests; add focused tests for direction, interruption, motion modes, card filtering/capping, outgoing fade lifetime and native-airspace cuts. No new windows or dialogs, so no new panic registration is required.
- Keep `shell-tab-navigation` **stub**, with one row and an explicit Missing list. Add numbered manual tester steps.

## WPF gap audit and remaining work

At the base, the row still lacked Full-motion slide/card stagger, outgoing fade, Dashboard Intake Pass refresh, Available Subjects StopPolling/lobby lease release, and real-desktop rail-hover evidence. The first two are implemented here. Door-header pulses, Lockdown stop/start, normal navigation, history/back, Blink Trainer refresh and entitlement repaint already existed and were retained. Visibility-owned shimmer/vault/skill-tree teardown also already existed. The base documents the old Presets and Deeper button handlers as dead XAML routes; this unit does not promote the row using that exception.

The following live WPF dependencies remain; this is a partial finish, not a claim of full parity:

- Intake Pass controller: WPF `MainWindow.UiUpdates.cs:1365-1900` includes niche art/help, available-only face selection, CTA breath and five half-turns (105/115/130/150/560 ms) with 8 s holds, state/mod subscriptions and visibility teardown. Core `IntakePassService` exists, but the Avalonia face is still render-only. `Services/Quiz/IntakeNiche.cs` also needs adaptation. Simply calling a nonexistent refresh or showing a static card would not implement this behavior.
- Lobby host: WPF `MainWindow.Lobby.cs` (293 lines), `Services/AvailableSubjectsService.cs` (395 lines) and `Services/Lobby/LobbyService.cs` (193 lines) supply the real polling/claim and watch-lease lifecycle, alongside game/account hosts. Core already has `LobbyModel` and `PbpLobbyApi`; it does not have the directory or aggregate polling service. Avalonia `AvailableSubjectsTabView` still uses demo rows and inert handlers. There is no live poll/lease to stop. A full port of these hosts plus the Intake controller and their behavior tests exceeds this unit's 1000-added-line budget; those dependencies remain named rather than inventing no-op teardown hooks.
- Real-desktop rail hover: not exercised in this headless environment.

## Verification limits

- `dotnet build CCP.Core` and `dotnet build CCP.Avalonia -c Release`: exit 0, zero errors.
- All eleven Linux test csproj files listed in `.github/workflows/build.yml`: exit 0, zero errors (single-worker builds). Initial disk-quota failures were retried after deduplicating generated asset copies.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors, 512 existing warnings. Windows code compiled but was not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 2811 passed, 5 Vosk-model tests skipped, 0 failed. Initial disk-full run discarded and rerun after recovering space; no tests changed or skipped by this unit.
- Focused filter `TabTransitionsTests|NavigationEntryParityTests|NavDoorRailGateTests`: exit 0, 8 passed, 0 failed, including the added snapshot-settle assertion.
- Expanded filter including `AssetsTabTests|CompanionPersonalityZoneTests`: exit 1; all 16 cases fail at shell construction with the environment's known `Could not create glyphTypeface` for Segoe UI Black. Their click/hover assertions were not reached. The focused run in a fresh process uses the app's font setup and passes. Full-suite pointer interaction coverage still needs the font-capable runner.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable (exit 127); this base does not contain the script. Added the numbered tester steps anyway.
 Windows desktop behavior, real pointer hover, and external lobby/account services are not run here. The changed motion is reached through real ShowTab navigation; headless tests use isolated user data via the existing suite setup.

## Assumptions

- A frozen outgoing frame is the smallest safe Avalonia equivalent of WPF re-showing a collapsed outgoing page. An unmeasured page has no frame and cuts immediately; native-airspace tabs also cut, matching WPF's exception.
- Keep the row stub until the remaining hosts and desktop hover are covered. Prioritize the user-visible motion gaps within this bounded unit.
