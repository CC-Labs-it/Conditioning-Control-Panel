# fin4-views-tab-exclusives

- Restored WPF shelf hover lift (1.02, 150 ms quadratic-out, snapped at Motion Off) and bloom (0.55, 24px maximum, performance gated). Bloom uses a sibling BoxShadow and reads the current mod colour on each enter, without adding a live Effect beneath the ambient layer.
- Restored the header's mod-aware vault painting, fade and shipped tier plate pictures, retaining the existing entitlement opacity/breath targets. Reused Core FavoritesRailArt candidate ordering and head ModArt; capped backdrop decode at WPF's 1400px.
- Added focused headless regressions for unavailable-card hover/reset, Motion Off, performance mode and header art. Added numbered manual steps and replaced the duplicated historical ledger text with a current, honest stub row.
- No trunk code imported. Read-only source branch inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation follows WPF in pinned base `df5a59167c58ab3735c2a5295db3b3b7243af475`.

## WPF gap audit

Read `Views/Tabs/ExclusivesTabView.xaml{,.cs}` and the delegated `MainWindow/MainWindow.Exclusives.cs` in the WPF reference. At the base, missing user-visible behaviours were card lift/bloom, header vault/tier artwork, the I just subscribed row (checking/disabled state, result and exception copy, effective-tier visibility), Account & Plans mode (header localization, hide shelf/spotlight, invite placement, See Premium navigation, outer scroll handoff), and greyscale locked invite rewards. Missing live paths also include Back Room, Breakout, Goon, Down the Rabbit Hole, Arcademy and Focus Gaze's Play switch. The Premium repaint does not opportunistically refresh the daily-free server override as WPF does; startup refresh and existing day-change repaint remain.

This unit closes lift/bloom and header artwork. All other items above remain missing; the ledger stays `stub`. The Account & Plans work depends on a real settings host and the live entitlement sync/activation lifecycle, not just a rendered button. Four WPF host sources alone total 7,237 lines (BackRoomHostService 778, BreakoutHostService 18, ArcademyHostService 6,140, GoonGameService 301), before their window/service integrations and behaviour tests. Porting all remaining doors correctly cannot fit this unit's 1,000 added-line limit. Existing unavailable cards retain their honest tooltips.

No new window, notification or overlay surface was added: this remains content in the existing MainShellWindow, so no new panic registration is needed. Existing Fluent icons remain the accepted divergence.

## Verification limits

- SDK: .NET 10.0.100. Channel discovery returned HTTP 403; the fixed-version install succeeded.
- Original snapshot was read-only and at a different commit. Used the requested clone fallback, verified the exact pinned base and clean state, then created only the requested unit branch.
- Initial parallel validation exhausted disk space; removed generated test outputs and repeated builds sequentially. Core suite retry: 4,117 passed, 5 skipped, 0 failed. Skips are the unavailable Vosk model tests.
- Before-fix proof: temporarily restored the two view files from the pinned base and ran `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ExclusivesFinishTests` with `DiffEngine_Disabled=true`. Both tests failed at the intended assertions: missing header image source/control and missing card ScaleTransform. The implementation was restored in a `finally` block.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors. All workflow Linux test projects built successfully: Core, Core.Settings, Core.Content, Avalonia.Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings and Snapshot. Avalonia.Tests final result recorded below.
- WPF tests built successfully with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors, 559 warnings. Windows code was compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The base has no `.github/scripts/test-plan-coverage.sh`; added the required numbered `WPF` / `Rows` coverage directly.
- No live invite server, Windows desktop behaviour or manual visual inspection was exercised; no render-all was required because no P122/dead-caller promotion was made. The full Avalonia suite was not run, only the relevant classes.

- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, 289 warnings, 0 errors (also rebuilt the final Avalonia app).
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~ExclusivesFinishTests|FullyQualifiedName~ExclusivesVaultTests|FullyQualifiedName~ExclusivesVaultMotionTests|FullyQualifiedName~ExclusivesAccentTests'`, `DiffEngine_Disabled=true`: exit 0; 9 passed, 0 failed, 0 skipped. This environment reached the behaviour assertions successfully; the previously reported font limitation did not occur. Tests use the assembly's CCP_USERDATA_DIR sandbox.
- Push failed: Git could not obtain HTTPS credentials. No authentication workaround or other branch push was attempted.

## Assumptions

- Preserve unavailable destinations rather than adding misleading navigation or partial game hosts. Keep `stub`; do not claim complete parity.
- Use the shipped tier pictures inside the existing breathing Borders, preserving the existing head motion ownership without a new abstraction.
