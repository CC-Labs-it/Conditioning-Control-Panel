- Implemented the live roster personality JSON picker and import/assignment path from WPF, including filename-based labels, new installed IDs, active-companion immediate activation, subsequent roster-switch activation, mod-aware labels/tooltips, cancellation, errors and success feedback.
- Reused the installed-prompt loader and Core explicit-content/validator/moderation rules. No new window class, animation, timer or service interface. Messages use the existing MessageDialog surface; the native picker uses Avalonia's storage-provider seam.
- Added focused Core tests for import, inactive assignment, activation, explicit gating, invalid input, missing/corrupt installed files and soft validation advice; added a sandboxed headless roster test for labels/tooltips, switching and stale-label clearing.
- Corrected the ledger's stale ConversationPage-mount claim, retained `stub`, and added numbered manual tester steps.
- Source: WPF reference at base `4702e2e55cd05db6c7d3208fa1ec372f4f106837`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from trunk.

## Gap analysis

The tab's WPF responsibilities are room ownership/runtime synchronization, original-name compatibility access, v2 page mounting and effective-visibility forwarding. Visibility, v2 mounting, live zone viewmodels and roster avatar switching already existed on the base. This unit adds the missing roster prompt path, rather than duplicating those ports.

Remaining WPF behavior/dependencies:

1. Aggregate runtime/zone interface and compatibility synchronization: hero/status/help/XP flash, AI permission controls, roster, behavior sliders/toggles/shortcut labels, trigger interval/presets, video-link pool, community rows and awareness cooldown/privacy controls. WPF forwards these to the same room or shelf controls, never dummy controls. Avalonia owns live per-zone implementations but lacks the aggregate and all corresponding shell write consumers. The seven legacy-partial claim in the row is historical: current WPF references also occur in Assets, SessionIO, TabNavigation and UiUpdates.
2. Community browse/download and offline/cache/error handling; the WPF browse handler installs the first uninstalled manifest entry. Community import/export/refresh buttons still have no host subscribers; installed prompt rows need name/author/active presentation, activation acknowledgement, uninstall and empty/refresh states. Sources: `MainWindow/MainWindow.Patreon.cs:2360-2470`, `MainWindow/MainWindow.CompanionTab.cs:175-322`, and `Services/Companion/CommunityPromptService.cs`. Only local import/lookup/assigned activation needed by roster is ported here.
3. Personality Studio expert draft/save/update/delete path is absent. The WPF tab installs its callback; normal `ConversationPage.OpenSheet` navigates to registered pages before reaching the fallback sheet that calls it. No claim that the fallback callback is a live production path, and no P122 promotion claimed.
4. Single-emote-mod pin during tube switching depends on unported emote playback/decoder. Static avatar switching is already implemented and remains intact.

These are not all solvable by wiring the tab alone. The remaining community service (500 reference lines), community UI/handlers (over 280 reference lines), Studio (177), aggregate (114), compatibility accessors and consumer adaptation, plus focused tests exceed the 1000-added-line unit cap as a complete port. This unit prioritizes a complete user-visible roster assignment path; it does not claim full-row parity or verification.

## Verification limits

- .NET SDK 10.0.402. The channel installer hit an unavailable CI feed; installing the resolved version from the standard builds feed succeeded.
- All 14 required Linux project builds: exit 0, zero errors (existing warnings). Includes CCP.Core, CCP.Avalonia and every Linux-compatible test project named in build.yml.
- Core focused tests: exit 0, 5 passed, 0 failed.
- Full Core suite (`DiffEngine_Disabled=true`, Release, `--no-build`): isolated final run exit 0, 3961 passed, 0 failed, 5 existing Vosk skips (3966 total). A concurrent-build run failed the existing board-frame budget test at 12.25 ms; rerunning without competing builds passed in 37 seconds. No tests were weakened or disabled.
- Core guards: exit 0, all guards passed. Initial read-only-sandbox invocation could not create temporary files; reran with required filesystem access successfully.
- `git diff --check`: exit 0.
- Test-plan coverage script is absent on this base; numbered WPF/Rows steps were added regardless.
- CCP.Avalonia.Tests compiled with zero errors. Relevant `CompanionRosterSwitchTests` and `CompanionTabVisibilityTests` execution deferred per the supplied cloud font-creation limitation; no desktop tests were run. No claim of fail-proven headless integration here.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 existing warnings, 0 errors. Windows runtime, native picker, manual UI steps and render-all were not run.

## Assumptions

- All roster companions are unlocked: WPF `CompanionService.IsCompanionUnlocked` always returns true. No new lock dialog was introduced.
- Keep the current voice when a companion has no valid assignment, matching WPF ApplyCompanionPrompt.
- Preserve WPF's refusal to activate an explicit prompt without existing acknowledgement; assigning a roster prompt does not introduce a new consent modal.
- Use the existing storage-provider seam and MessageDialog, rather than adding a window or a one-implementation abstraction. No new media/overlay surface requires panic registration.

## Commands

- `dotnet build <project> -c Release`: exit 0 for each project below.
  - `CCP.Core/CCP.Core.csproj`
  - `CCP.Avalonia/CCP.Avalonia.csproj`
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~InstalledCommunityPromptsTests`: exit 0, 5/5 passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final isolated exit 0.
- `bash .github/scripts/core-guards.sh`: exit 0.
- `git diff --check`: exit 0.
