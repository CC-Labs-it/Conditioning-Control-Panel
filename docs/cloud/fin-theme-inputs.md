- Finish the `theme-inputs` user paths from the pinned base `92e0117aa19447615f7deb9fc0e605080a4eda78`.
- Bind implicit ComboBoxItem alignment to the logical ItemsControl ancestor, including changes while the dropdown is open. Local row values and custom ItemContainerThemes retain precedence.
- Add WPF popup motion through the existing Fluent parts: full-height Slide on combo opening; Fade on submenu opening and closing; linear 150 ms endpoints. Reverse the slide when placement flips above the combo. Reopening cancels an old close, native close updates the owner, and the fading submenu stops hit testing immediately.
- Enable ContextMenu's native window-manager shadow hint, corresponding to WPF HasDropShadow. Retain transparent rounded popup hosts and use no animated Effect.
- Apply WPF nested submenu offsets (+4, -6), retaining the top-level menu's bottom placement.
- Give Deeper's keyed EditorComboBox its existing EditorComboBoxItem theme, matching the WPF keyed style. Other keyed combos already supply their own item themes; do not impose a global styled Theme on rows.
- Register pending input popups in PanicSurfaces. Panic, owner detachment and theme replacement cancel their animation work immediately.
- Add six focused tests covering these paths, animation configuration and deterministic completion/cancellation. The initial three regression tests failed before the fixes (alignment Left rather than Right, shadow hint false, missing editor row theme), then passed. No existing test source was weakened, disabled or deleted; environment-driven Vosk skips are recorded below.
- Source credit: read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; Inputs.xaml last changed there in `92dad0b7c7f18c1f8b6ee5078273d6587ea96940`. No trunk Avalonia code was imported or existing files overwritten. Adaptation uses the WPF reference already present on the required base, preserving earlier lane work.

## Gap inventory and assumptions

The base ledger's remaining gaps were popup Slide/Fade, ContextMenu HasDropShadow, nested submenu offsets, parent-bound combo row alignment, and keyed combos that left Fluent rows in place. All have live user paths through implicit combo/menu themes and the Deeper editor's keyed combo.

- WPF `Placement=Right` top-aligns the popup. Avalonia's equivalent is `RightEdgeAlignedTop`; Avalonia `Right` would center vertically. Keep the WPF +4/-6 offsets.
- WPF's platform Popup implementation uses 150 ms linear animation. The port animates the child Visual, retaining Fluent's popup/keyboard/light-dismiss machinery. Tests control completion and assert configured endpoints/duration; they do not claim measured frame timing.
- HasDropShadow maps to the existing native shadow hint. Actual shadow appearance and compositor placement remain platform verification limits.
- The only existing named keyed combo theme without an ItemContainerTheme was Deeper's EditorComboBox. WPF supplies EditorComboBoxItem there, so use that existing custom head theme rather than the implicit dot rows. Anonymous shell combos already set their own ItemContainerThemes; DarkComboBoxStyle also does.
- WPF ListBoxItem's parent-alignment binding was checked as well. Avalonia ListBox does not expose those parent content-alignment properties. Live WPF lists using the Inputs row template keep its default Left/Top alignment, which the head already matches. The MediaHistory list's explicit Stretch alignment has its own local row style. There is no live nondefault parent-alignment path through the Inputs template requiring another surface/API.
- Keep `wired`, never `verified`: desktop/IME, native shadows and platform animation presentation cannot be established by a Linux headless run.

## Verification limits

- SDK: .NET 10.0.402. Initial cloud checkout had a different commit and read-only Git metadata; used the explicitly authorized temporary clone, verified the exact required base, then created only the requested feature branch.
- All 14 required builds succeeded (exit 0, zero errors): Core, Avalonia, the 11 Linux workflow test projects, and the WPF test project with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Existing warnings remain (including 512 in the WPF build).
- Build command: `dotnet build <project> -c Release`, with the four SDK `CreateHardLinksFor*IfPossible=true` copy options. The first sweep exhausted disk while duplicating assets; after removing only generated build-only test outputs, the complete rerun passed. No project settings or sources were changed to bypass a build.

| Build project | Exit | Errors |
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

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` Both forbidden-API and XAML type-reference checks passed. The initial read-only invocation could not create its temporary files; the writable rerun and final rerun passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; 2811 passed, 0 failed, 5 skipped, total 2816. Existing speech integration tests auto-skip without the Vosk model. The environment documents its model download as blocked with HTTP 403; speech recognition was not validated and no tests were edited to hide that limitation.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~InputPopupThemeTests`: exit 0; 6 passed, 0 failed, 0 skipped. Uses bundled Inter and the existing sandbox profile. Its editor regression checks the real control/resource theme without rendering unrelated font-dependent content. Animation completion is controlled through a test seam, while real selector/binding/popup paths execute.
- Existing InputChromeThemeTests, InputTextBoxThemeTests, other Avalonia classes and the full Avalonia suite were compiled but not run here, per the documented missing-font limitation. The new focused suite supplies bundled Inter; no global font workaround or existing test changes were added.
- Isolated-profile `dotnet run --no-build --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --render-all <output>`: exit 0; `224/224 views rendered`, no binding warnings. DeeperEditorWindow.png inspected; it uses the production font mappings and renders correctly. The intentional unavailable loopback showcase endpoint logged a connection-refused warning.
- Windows head and WPF tests compiled but could not execute on Linux. Native menu shadows, Windows desktop/IME, compositor edge placement and perceived animation timing were not manually verified. `wired` records reached behavior, not visual verification.
- No new content surface/window was introduced; existing input popups additionally receive explicit panic teardown registration. No Core source changed.
