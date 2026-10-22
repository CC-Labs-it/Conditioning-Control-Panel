# sync7-friends-link

- Adapted WPF `75503e346`, `018075f1c`, `0375913ba`, `5d7c2110a`, `c3efdb257` to the existing Avalonia drawer/landing. No trunk code imported; `parity/trunk` fetched read-only.
- Copy link sits beside the account's code; New link lives in the add box. Code/token/message-link normalization delegates to the existing Core FriendsLinkRules. Claim delegates to Core FriendsService and refreshes once.
- Link clipboard writes have an asynchronous test seam. Copy/revoke presses serialize. Copy asks Core for the live link every time, falls back to the code, and words the note from remaining expiry/uses. The note fades at WPF timings.
- Online rows show ordered Fluent device marks, with tooltips. Race invites join the web race through the existing ExternalOpener gate/fallback. Breakout cards name the destination and remain unjoinable while their full host is absent.
- `FriendsLinkPortTests` uses the real Core service with an in-process HTTP fake; no account, server, browser or desktop clipboard is used. Core grammar and service logic stay in Core.

## Verification limits

- SDK `/workspace/shared/dotnet` 10.0.402; `DiffEngine_Disabled=true`. Compiler processes were bounded with `DOTNET_PROCESSOR_COUNT=2`, `-m:1 -p:UseSharedCompilation=false`. Disk-constrained builds used `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyAdditionalFilesIfPossible` set to true.
- `dotnet build CCP.Core`, `Tests/CCP.Core.Tests`, `Tests/CCP.Core.Settings.Tests`, `Tests/CCP.Core.Content.Tests`: succeeded, 0 errors. The seven other Linux workflow test projects also built successfully: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot. These assemblies were compiled only (`BuildProjectReferences=false`, Core/head built separately), not executed.
- `bash .github/scripts/core-guards.sh`: passed, 429 XAML references checked.
- `dotnet test Tests/CCP.Core.Tests --no-build`: passed 2845, failed 0, skipped 6, total 2851. Five speech tests skipped without the Vosk model; the existing BoardTiles frame-budget test also skipped.
- Baseline proof: `DrawerOffersCopyAndNewLinkAndAcceptsPastedTokens` failed at the absent Copy link button (1 failed, 0 passed). The base drawer binary was compiled with the unused link test seam present; retry after a disk-full copy failure used the already-built baseline head and hard links. Final source was restored for the normal build.
- Broader existing `FriendsDrawerTests|KnockCardTests`: 18 passed, 5 failed (three glyphTypeface/bootstrap failures, two headless window ownership/placement failures). The same 18/5 result was reproduced against the saved baseline drawer binary. A temporary fontconfig pointing at bundled Fredoka/Noto Mono did not resolve the default-font/owner failures. Existing tests are unchanged.
- WPF test build attempted with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`, plus bounded/hard-link flags. Restore succeeded; the 180-second bound expired during WPF temporary-project compilation (exit 124). No WPF tests ran.
- Initial `--render-all` produced 138 PNGs before exit 135 while another build could rewrite its hardlinked assembly; that attempt is not treated as reliable final render evidence. Final isolated render result is recorded below.
- Corrected focused run: `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~FriendsLinkPortTests|FullyQualifiedName~IconGuardTests|FullyQualifiedName~SandboxWhitelistTests'` (bounded compiler and four hard-link flags): passed 19, failed 0, skipped 0. This includes all eight new link/platform/race/layout tests. The new suite initializes the shipped headless font setup and waits for real deferred text-change events.
- `/tmp/worker6-friend-links.png` was exported by `LinkControlsFitTheNarrowDrawer` and inspected: Copy link and New link fit the 300px drawer. No live account is used.

- Final explicit `dotnet build CCP.Avalonia/CCP.Avalonia.csproj` and `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` (same four hard-link flags): both succeeded, 0 warnings / 0 errors on the incremental final checks.
- Final isolated `timeout 300s dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker6-render-final`, with `CCP_USERDATA_DIR=/tmp/worker6-render-final-profile`: reached the 300-second bound (exit 124), produced 127 PNGs through LoginDialog, including `FriendsDrawer.png`. Full render-all completion is not claimed; the inspected signed-in fake-wire drawer is `/tmp/worker6-friend-links.png`.
- Added lines vs BASE: 509.

## Remaining slice (`sync7-friends-link-next`)

- Sync rows 287 and 289 remain needs-port: the existing drawer juice stub has no shared Pop/sparks, claim shockwave or drawer click/denied/accepted sound cues. Link operations and timed note fades work; replacement-pill Pop is not claimed.
- Sync row 288 remains needs-port: finish Breakout full/demo join selection after its full host is available. Do not send a Joined receipt for a destination this head cannot open.
- Parity `ctrl-friends-drawer` and `feat-friends-landing` remain stub. Existing unrelated drawer/landing gaps are unchanged.
- Live signed-in server behavior, actual clipboard/browser fallback and Windows execution require desktop/account checks outside this Linux session.
