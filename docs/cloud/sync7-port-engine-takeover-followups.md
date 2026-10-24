# sync7-port-engine-takeover-followups

Base: `39260f198fad67d90bc988c63a00480830e5fefc` (`avalonia-port/main-20261024-compat`).

- Adapted WPF/parity source `dce9652cc8ca6d47897a5c87fa887a4631de163d`: recheck both Mind Wipe switches after the announcement and again at UI delivery. Existing selection gating is retained.
- Adapted WPF/parity source `cb8b2aacc605847ee5b453bcb6db0e1932095ea3`: route random Takeover comments through the companion brain's ambient event path, preserving history and non-interactive moderation options; use the corrected stateless prompt when the brain is unavailable/off; drop blank and consecutive duplicate comments. Updated the Remote chip and tooltips to the WPF copy.
- Added focused Core regressions and updated headless Takeover/Remote assertions. No new surface, so no additional PanicSurfaces registration is needed. No sync ledger or parity matrix changes.

## Commit disposition

### `dce9652cc` — ported in part; wallpaper blocked-by-host

- Ported: `CCP.Core/Services/Autonomy/AutonomyScheduler.cs:352` and `CCP.Avalonia/Views/Windows/MainShellWindow.Autonomy.cs:142`. Tests: `AutonomySchedulerTests.AnnouncedMindWipeRechecksBothSwitches` (two disabled combinations and the positive case), `TakeoverTests.MindWipeRechecksSwitchesAfterUiPost`.
- Already covered: selection gate at `CCP.Core/Services/Autonomy/AutonomyScheduler.cs:114`; removed VIP/dead link pools at `CCP.Core/Models/BuiltInMods.cs:1183`, `:1534`, `:2311`, and `CCP.Core/Services/HypnotubeDefaultLinks.cs:94`. Core's companion consumes the shared pools (`CCP.Core/Services/BambiSprite.cs:962`), and the head seeds that same title table (`CCP.Avalonia/App.axaml.cs:173`). None of the twelve removed URLs remains in these pools; no pool changes made.
- Blocked-by-host: ordered wallpaper writes, nonblocking Win32 notification, busy activation/shuffle gate, recovery breadcrumbs and bounded shutdown flush require a wallpaper service/platform adapter. This head has none and excludes `WallpaperShuffle` in `MainShellWindow.Autonomy.cs:13` and its `CanPerformAutonomy` default. No unused queue or new wallpaper host was introduced.

### `cb8b2aacc` — ported; shared portions already covered

- Ported comment behavior: `CCP.Core/Services/Autonomy/TakeoverComment.cs:11`, called by `CCP.Avalonia/Views/Windows/MainShellWindow.Autonomy.cs:201`. Tests: `TakeoverCommentTests.RoutesThroughAmbientBrainOrTheRewordedStatelessPrompt`, `DropsBlankAndOnlyTheLastCommentIgnoringCaseAndOuterSpace`.
- Ported Remote copy: `CCP.Avalonia/Views/Windows/MainShellWindow.DirectoryPill.cs:15` and `MainShellWindow.axaml:307`. Tests: `RemoteDirectoryTabTests.Pill_states_match_wpf` plus its existing session lifecycle test with updated literal assertions.
- Already covered: ambient descriptor `CCP.Core/Services/AIService/FrameFormatter.cs:146`; dash word breaks `CCP.Core/Services/LockCard/LockCardText.cs:118`. Existing `LockCardTextTests.A_dash_is_ignorable` and `The_typed_length_still_counts_the_dash` preserve matching and length-budget behavior. No changes to these shared portions.

## Verification limits

- .NET SDK 10.0.402. The initial channel lookup encountered a 403; installation ultimately succeeded (10.0.100 was also installed as a fallback).
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: zero errors.
- Every Linux test csproj named in `.github/workflows/build.yml` compiled with zero errors: Core, Core.Settings, Core.Content; Avalonia, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot.
- `bash .github/scripts/core-guards.sh`: passed. `git diff --check`: passed. `test-plan-coverage.sh` is absent on this base; invocation returned 127. No ledger rows were added or promoted.
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,960 passed, five Vosk-model-dependent tests skipped, zero failures (3,965 total). No tests were altered to skip.
- Focused Core tests: exit 0, 21 passed. Negative control temporarily removed the production recheck, routing, fallback wording and repeat suppression, then restored all production sources: exit 1, six failures / 15 passed, proving the guards, route and copy tests detect their absence.
- Avalonia regressions compiled but were not run under the documented cloud font-creation limitation. CI must run `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~TakeoverTests|FullyQualifiedName~RemoteDirectoryTabTests'` with `DiffEngine_Disabled=true`. No real desktop/profile was used.
- WPF test compilation was attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; exit 0, zero errors (557 existing warnings). WPF/Windows behavior was compiled but cannot be run on this Linux worker.

## Assumptions

- Preserve the existing head's stop/panic check before showing an awaited comment, and keep duplicate state per shell rather than global, matching the WPF service lifetime.
- The existing shared Hypnotube title table is the counterpart of the frozen WPF tube's hardcoded pool; do not recreate a second table.
- The missing wallpaper host is outside this follow-up unit, as required by the task's host rule.
