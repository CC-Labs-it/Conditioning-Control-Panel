# sync7-port-conversion-lapse

- Adapt WPF `a54ce18f51b8bc805421d89fa9881b7926ce591b`: Core lapse decisions and per-grace-end/day stamps; a neutral, singleton Avalonia notice card with close-before-action handling and panic registration. Reuse the existing PlanCatalog compiled fallback for checkout links.
- Adapt WPF `401d13aed3f2460dd1f96cfbe4926caef6263695`: lapse/reconnect Inbox rows through StartupLadder, checked on shell focus, successful profile load and lapse enforcement. The existing nine-language copy is reused. Inbox glyphs now render through the existing Fluent icon map.
- Source fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Only the two requested WPF commits were adapted; no sibling conversion work imported.
- Core lapse types use a distinct Services.Conversion namespace so the frozen WPF types remain unambiguous in WPF tests. The existing game-screen probe now feeds StartupLadder quiet routing.
- No sync ledger or parity status changes.

## Per-commit disposition

- `a54ce18f5`: ported available hosts in `CCP.Core/Services/Startup/Conversion/ConversionRules.cs:8`, `CCP.Avalonia/Views/Dialogs/InboxNoticeCard.cs:16`, `CCP.Avalonia/Platform/AccountSeed.cs:174`. Tests: `ConversionLapseRowsTests`, `AccountSeedTests` (ConversionLapseTests.cs), `AccountSeedTests.ProfileReadingRemembersLowerClearsAgreementAndPreservesNoSignal`. Already covered on base: `CCP.Core/Models/AppSettings.cs:3367` (ServerTierSeen), `:3392` (InboxStamps), `CCP.Core/Services/Account/EntitlementTierRule.cs:76` (ServerTierSeenAfter).
  **blocked-by-host (remaining live sync portion):** Avalonia has no EntitlementTierSync service or focus-profile poll. Core V2AuthService.SendHeartbeatAsync returns only success, discarding the response body; there is no heartbeat tier-reading callback. The existing authenticated profile-load host now remembers/clears the signal before ApplyRise and checks rows after save. Creating the absent live-sync service belongs to its host lane.
- `401d13aed`: ported available hosts in `CCP.Avalonia/Views/Windows/MainShellWindow.Conversion.cs:16`, `MainShellWindow.Inbox.cs:38`, `MainShellWindow.Patreon.cs:151`; tests `AccountSeedTests` (ConversionLapseTests.cs) cover quiet routing, deduplication, closed/reconnect rows and card lifecycle. Already covered: nine-language `conv_lapse_*` copy (e.g. `CCP.Core/Localization/Languages/en.json:8578`).
  **blocked-by-host (long-absence greeting):** `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs:281` explicitly leaves the greeting timer and Speech partial unported. No replacement greeting host or unused greeting helper was invented.

## Verification limits

- SDK: .NET 10. Channel discovery initially returned 403; pinned 10.0.100 installed successfully, and channel installation later completed with 10.0.402.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 12 Linux-compatible CI test projects compiled with zero errors. Auxiliary builds retried with `-m:1` and sequential output cleanup after initial disk exhaustion.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 3,973 passed, 5 existing Vosk skips, 0 failed. Initial run during disk exhaustion/build contention had 3 failures; clean-space retry passed without test changes.
- Focused lapse tests: exit 0, 20 passed, including a repeat after the namespace adjustment. Negative control changed the new three-day bucket line to two days: exit 1, 1 failed (expected Warn3, actual Warn7), 19 passed; correct production line restored before the green full suite.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter "FullyQualifiedName~ProfileReadingRemembersLowerClearsAgreementAndPreservesNoSignal|FullyQualifiedName~LapseRowsWaitWhileAGameOwnsTheScreen"`: exit 0, 4 passed on final sources. This filter opens no windows.
- Window-based `ConversionLapseTests.cs`, Inbox and panic tests compiled but were not run: this base lacks the cloud-test-fonts fix; the task specifies compilation only for those tests in this environment. Headless tests use the existing isolated profile and in-memory secrets fixture. No desktop or real account was used.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this base. Added a numbered manual test step; no ledger rows promoted.
- `git diff --check`: exit 0.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. First completed attempt caught the new Core/WPF type collision; the distinct Core namespace fixed it without editing WPF. Windows code compiled but was not run on Linux.

## Assumptions

- Keep the WPF compiled plan checkout fallback for these rows; reuse PlanCatalog.Fallback instead of duplicating URLs.
- Preserve the existing profile-read host and its authentication guard. Do not import sibling live entitlement-sync or companion speech infrastructure.
- Use the existing Fluent mapping for Inbox glyph data, preserving older row data.
