# fin-shell-companion-fx

- Finish the shell's mod repaint: all five roster cards display the assigned installed prompt's mod-aware name and localized AI-personality tooltip. Missing, cleared, empty, null-JSON and corrupt prompts clear both. Refresh on room show and the existing UI-dispatched mod-change hook.
- Reuse Core `InstalledCommunityPrompts.Load`, already shared by the frozen WPF service and Avalonia prompt editor. No new service, timer, effect, window, or panic surface; no new panic registration is needed.
- Add a shell-path regression covering all five assignments, mod vocabulary changes from a background event, and stale-label/tooltip clearing on return to the tab. Its app setup uses bundled Inter and the existing isolated profile.
- Mark only `shell-companion-fx` wired. The separate `shell-companion-tab` assignment/import/activation flows are outside this row.
- Imports: none. Reviewed read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its FX partial still lacks prompt-label repaint. Existing base files and helpers take precedence.

## WPF inventory and gap analysis

Reference pinned at base `20767177a4c9186c2b81cc4dff416fb85fd158df`:
`ConditioningControlPanel/MainWindow/MainWindow.CompanionFx.cs`, with its live callees
`MainWindow.CompanionTab.cs:464` and `Services/Companion/CompanionService.cs:452`.

| WPF behavior | Base and disposition |
| --- | --- |
| Visibility hook persists drawers on hide and offers Awareness v2 consent on show | Already reached through CompanionTabView; retained. Exit persistence and first-show restoration guard retained. |
| Lazy mod subscription; marshal background callbacks to UI | Already wired; retained, including unsubscribe on shell close. |
| Mod switch re-reads hero art/name/flavor/chip and roster support/name/accent/progress | Already wired through the hero's subscription and room.RefreshRoster; retained. |
| Mod switch re-reads all assigned prompt names, applies MakeModAware, and sets/clears localized tooltip | Sole remaining gap; add to the roster refresh using the exact installed-file reader WPF uses. Also reached on initial attach and tab show. |
| Former disc breathe and AI-card connect sheen | Removed in WPF, as the reference file explains. Hero owns its ambient clock; the Engine Room has no sheen. No additional shell FX to port. |
| Incoming-tab guard ignores the outgoing fade's re-show | This head has no outgoing fade re-show; no extra guard required. |

The prompt lookup dependency claimed in the ledger is stale: Core already contains
`InstalledCommunityPrompts.Load`, and WPF's `CommunityPromptService.GetInstalledPrompt`
delegates to it. The shell row has a live WPF caller, so P122 is not applicable.
Saved per-companion assignments from a migrated profile reach these labels without
requiring the still-unported personality-assignment picker.

## Verification limits

SDK: .NET 10.0.402. All test commands use `DiffEngine_Disabled=true`.

| Command | Result |
| --- | --- |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~CompanionPromptLabelsTests` before product fix | Exit 1: expected `First 0`, actual empty string. |
| Same filtered test after product fix | Exit 0: 1 passed, 0 failed, 0 skipped. |
| `dotnet test Tests/CCP.Core.Tests -c Release` | Exit 0: 3541 passed, 0 failed, 5 skipped (existing Vosk model-dependent tests). |
| `bash .github/scripts/core-guards.sh` after changes | Exit 0: all guards passed; 429 XAML references checked. |
| `dotnet run --no-build --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --nav-check` with temporary `CCP_USERDATA_DIR` | Exit 1: `OpenDeviceSettings reveals Devices (saw data)` and subsequent unknown-section expectation. These exercise Settings navigation outside this change; left unchanged. |
| `git diff --check` | Exit 0. |

Release builds (`dotnet build <project> -c Release`):

| Project | Result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | Exit 0; 0 errors. |
| `CCP.Avalonia/CCP.Avalonia.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | Exit 0; 0 errors. |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` (flags below) | Exit 0; 0 errors. |

The WPF test-project build also passed with
`-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
554 warnings, 0 errors. This compiled the Windows head and tests; neither was run on Linux.
Its transient NuGet 502 recovered during restore. All 15 requested builds exited 0.

The new regression ran headlessly with bundled fonts and the assembly's existing temporary
`CCP_USERDATA_DIR` sandbox; it neither reads the real desktop nor writes a real profile.
Other Avalonia tests were compiled, not run: their existing font setup is known to fail in
this cloud environment. The full Avalonia suite and Windows runtime behavior remain for CI.
No real desktop, notifications, live AI, or Windows execution was used. Five Core speech tests
skipped automatically because the Vosk model is unavailable here (the task records HTTP 403
for its download); no test was weakened or given a new skip.

## Assumptions

- Preserve this head's existing drawer restoration on each show (each hide saves first).
- Keep assignment/import/activation UI in the separate shell-companion-tab unit; display of existing assignments is independently reachable.
- No ambient or entrance animation is required by this row: the current WPF partial explicitly retired its two effects.
