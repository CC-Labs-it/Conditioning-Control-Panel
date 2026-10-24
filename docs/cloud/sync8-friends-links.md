# Sync 8: Friends watch links

- f37d8a58f5a915b8e07647de819b678494ee891d = already-covered (production): `CCP.Core/Services/Friends/FriendsLandingRules.cs:224` builds the playable `video-<id>.html` URL and preserves the ASCII-digit/8-character validation; `CCP.Avalonia/Platform/LeashTaskHost.cs:191` already consumes it. The base merge `28ab74eed692067fed4f2051791b3a6d0976b1dc` replayed this WPF fix into Core. Mirrored the WPF regression in `Tests/CCP.Core.Tests/FriendsWatchLinkTests.cs:10`, including exact URL, eligibility, ID round-trip, boundaries and invalid IDs.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. The only source adaptation is the WPF test from f37d8a58f above.
- No duplicate production URL change. Friends watch corner notices/reopening are already deferred on this head (`CCP.Avalonia/Platform/FriendsLanding.cs:233`); adding that larger feature is outside this URL-fix unit.
- Separate `compat-scratch: restore pure Core rules and remove head references` commit supplies four omitted pure rules and replaces three stale WPF singleton accesses; the WPF project excludes its duplicate rule files from compilation (reference sources preserved). Integration should drop that scratch commit when taking the compatibility lane.

## Verification limits

- SDK 10.0.100. Channel discovery hit HTTP 403; the fixed-version installer succeeded.
- Core Release build: exit 0, zero errors (existing warnings).
- Core architecture guards: exit 0, all guards passed after scratch repairs.
- Focused Core regression: exit 0, 1 passed. Replacing only the URL expression with the pre-f37d8a58f bare path produced exit 1, 1 failed (expected playable URL versus bare ID); the correct expression was restored immediately.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors (existing warnings).
- `dotnet build <project> -c Release`: exit 0, zero errors for every Linux test project named by `.github/workflows/build.yml`: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Migration,Report,Scheduler,Snapshot}.Tests (all under `Tests/`).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-restore`: exit 0; 3,954 passed, 0 failed, 5 skipped by existing Vosk model-availability checks. No test was weakened or disabled.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~FriendsLandingTests.AppStartupStartsTheLandingOnTheFriendsPollAndStopsItOnExit`: exit 0; 1 passed.
- Avalonia window-rendering Friends tests were compiled but not run, following this job's documented font-creation limitation. The URL regression runs entirely in Core without fonts.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release --no-restore -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (604 warnings). The first probe exposed duplicate types from the scratch copies; the scratch WPF compile exclusions resolved them. Windows tests were compiled but cannot be executed here.
- `git diff --check`: exit 0.
- No real browser/video request or desktop interaction is required by the regression. The live service's HTTP behavior is taken from the WPF reference.

## Assumptions

- Keep already-merged production behavior and add the missing Linux regression; do not broaden this lane into deferred Friends watch UI.
