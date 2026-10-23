# Finish Companion Room preview

- Seat a fresh, collapsed Workshop exhibit in all six page states. Preserve WPF's six-cell order, 25 rows, roster values, four slider fractions, caption and no-op commands. Hero Switch and awareness fine-tuning reach it through the existing room navigator.
- Adapt the WPF `MockWorkshopAccordionVm` reference from read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (the file matches the required base). Credits: depth-zone behavior `3068b9d26432f11e98c0d464dbcb932f72a3c202`, page composition `c2b1c133d0db508a2a00852c206134bd8bd0a023`, localized drawer note `c33700bd6ed25497a73f4612800a7773d2e8963a`.
- Enable the permissions exhibit with WPF harness defaults: master/effect boxes off, memory on, haptic slider 0.6 and label 60%. WPF handlers forward only from MainWindow; in the preview, edits are local and button handlers do nothing. Preserve edits across state swaps and ignore live settings refresh. Existing live-grid behavior remains covered by the existing chat-memory suite.
- Add focused tests for every Workshop row/command, fresh state bags and deep links, permissions defaults/local edits, unchanged settings, preserved conversation and absent dialogs. Update the earlier disabled-zone assertions to require enabled exhibits.
- Mark the single ledger row `wired` under P122, with the pinned no-caller proof below. Render-all succeeded for 224/224 views and the default preview PNG was inspected; its log has no binding errors. No new window, capture, player or effect surface was introduced; the panic starter-registration guard passed.

## Verification limits

- .NET SDK 10.0.402. `dotnet build <project> -c Release`: exit 0 and 0 errors for Core, Avalonia, and all 11 Linux workflow test projects: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests`. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, "All Core guards passed."
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0, 2811 passed, 5 skipped (Vosk model unavailable), 0 failed.
- Filtered `CCP.Avalonia.Tests` model/panic tests: exit 0, 2 passed, 0 failed. Filter: `FullyQualifiedName~CompanionRoomPreviewTests.WorkshopExhibitMatchesWpfRowsAndCommandsAreInert|FullyQualifiedName~PanicSurfacesTests.EveryStarterIsRegisteredOrAllowlisted`.
- Workshop mutation proof: changing the first roster value from WPF's Lv 12 to Lv 13 made the focused test fail (exit 1, expected Lv 12 / actual Lv 13); the source was restored and the final filtered rerun passed (exit 0, 2 passed, 0 failed).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final exit 0, "Build succeeded", 2 warnings, 0 errors. The first attempt exhausted disk space during runtime-file copying (exit 134); removing generated Avalonia outputs allowed an incremental retry to succeed. Windows code was compiled, not executed.
- `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all "$RENDER_DIR"` with a disposable `CCP_USERDATA_DIR`: exit 0, "224/224 views rendered". Default-state preview inspected; expanded Workshop and the other five variants were not rendered.
- Window-creating `CompanionRoomPreviewTests` and `HerRoomChatMemoryTests` were compiled but not run locally: the required base lacks the shared font-test setup described in the task. The supervisor must run those classes on GitHub (rcheck). Their red/green permission-guard proof is also pending there; the model-only proof does not cover those guards.
- WPF/Windows application execution and live desktop testing were not run on Linux.

## WPF gap inventory and assumptions

The base ledger and the cited WPF window were read at `101a824756de19dd56d990f8a104b9a95d8318f2`, alongside `MockCompanionRoomVm`, `MockWorkshopAccordionVm`, `AiPermissionsGrid` and `CompanionRoomPreview`.

1. Missing Workshop preview bag: the Avalonia drawer retained its live controls and was disabled. WPF uses `MockWorkshopAccordionVm.Collapsed()` for all six variants. Completed with synthetic rows and fresh state on every valid switch.
2. Permissions could save settings, wipe memory or open setup dialogs, so the whole grid was disabled. WPF preview has no MainWindow host and all shims are inert. Completed using a per-instance preview flag and WPF markup defaults. The panel stays collapsed and the 60% label stays static when its slider is driven, matching the no-op WPF shim.
3. The remaining normal-app opener is dead in WPF. Keep Launch/the state strip as the driver path; apply P122 rather than inventing a menu entry. The unused environment-gated MaybeShow wrapper stays unported.

The personality-assignment row omits WPF's decorative personality-assignment emoji, consistent with this head's existing Fluent icon policy. Text, ordering and values otherwise match the WPF mock. Mock invocation-tag diagnostics have no consumer on this head; the existing relay command provides the required no-op behavior.

## P122 pinned no-caller proof

Run against the required base, restricting the search to the WPF product tree:

```sh
git grep -n -E 'CompanionRoomPreview(Window)?\.(Launch|MaybeShow)|new CompanionRoomPreviewWindow' 101a824756de19dd56d990f8a104b9a95d8318f2 -- ConditioningControlPanel
```

The only executable matches are `CompanionRoomPreview.cs:67,71` (Launch inside MaybeShow) and `CompanionRoomPreviewWindow.xaml.cs:57` (construction inside Launch). All other matches are comments in those files or the window XAML. In particular, there is no executable caller of MaybeShow, no shell construction of the window, and no normal-app Launch caller. `CompanionRoomPreview.cs:14` explicitly documents "Nothing calls it today". The existing render-all driver constructs the Avalonia preview as one of its 224 views.
