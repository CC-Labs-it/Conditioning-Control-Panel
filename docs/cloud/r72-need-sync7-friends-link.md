# Friends links re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`, clean `work` checkout.
Source: `c2bc2f5aaa47..fde4671fb531` (fetched and inspected).
No WPF or Core behavior changes. All reused keys exist in all ten languages.

| Behavior | Status | WPF specification | Avalonia evidence |
| --- | --- | --- | --- |
| Copy live link, remaining days/uses, offline code fallback, failure note | Ported | `ConditioningControlPanel/Controls/Friends/FriendsDrawer.Link.cs:100` | `CCP.Avalonia/Views/Controls/FriendsDrawer.Link.cs:66` |
| Retire old link and copy new one; serialize copy/new requests | Ported | `ConditioningControlPanel/Controls/Friends/FriendsDrawer.Link.cs:37` and `:132` | `CCP.Avalonia/Views/Controls/FriendsDrawer.Link.cs:87` |
| Paste first-party link/token, preserve typed CCP token, claim once, retry failures | Ported | `ConditioningControlPanel/Controls/Friends/FriendsDrawer.cs:1030`, `.Link.cs:150` | `CCP.Avalonia/Views/Controls/FriendsDrawer.cs:109`, `.Link.cs:103` |
| Online desktop/phone/web marks, stable order, tooltip and accessible name | Ported | `ConditioningControlPanel/Controls/Friends/FriendsDrawer.Platforms.cs:18` | `CCP.Avalonia/Views/Controls/FriendsDrawer.cs:367` |
| Token grammar, copy choice, claim refresh, platform ordering | Already in shared Core | WPF calls `FriendsLinkRules` and `FriendsDrawerRules` from the files above | `CCP.Core/Services/Friends/FriendsLinkRules.cs:25`, `FriendsService.Link.cs:18`, `FriendsDrawerRules.cs:92` |
| Race/Breakout names, joining and receipts | Covered by other old lane, excluded here | `ConditioningControlPanel/Services/Friends/FriendsLanding.cs:204`, `:565`, `:572` | `04df9e0812b7`, `CCP.Avalonia/Platform/FriendsLanding.cs:143`, `:266`, `:288`; base remains Ramp-only at `:267` |
| Tier plates, lock chips, notices, requester reports | Covered by open branch, excluded here | `ConditioningControlPanel/Controls/Friends/FriendsDrawer.Honesty.cs:80` and drawer head | `avalonia-port/cloud-r72-fin3-ctrl-friends-drawer` at `8882ccf2ea66`, `FriendsDrawer.cs:200`, `:358`, `:493`, `:664` |

The old feedback lane `8ed2151c6a55` also overlaps copy/new/claim. Its own change was inspected. This cut retains the source lane's request serialization and reentrant text normalization guard. Shared juice and FriendsSfx are outside this gap.

Focused tests: `Tests/CCP.Avalonia.Tests/FriendsLinkPortTests.cs:74,103,137,159,175,190,211,233`.
Fake wire and clipboard, no desktop clipboard or browser; standard Skia headless dispatcher setup.
Fixtures use `fake-token`, `FAKEFAKEFAKE` and `TESTTESTTEST` (valid grammar, visibly fake).

Validation commands and result tails follow. SDK: `/tmp/dotnet`, 10.0.403; PATH and DOTNET_ROOT set for xUnit child processes.

Part B: no remaining copy/new/claim/platform gap. Race/Breakout joining stays with
`sync7-port-friends-breakout-join`, as above. Shared sound and burst feedback remains
missing here: WPF `FriendsDrawer.Link.cs:110,114,126,140,166,173`; Avalonia
`FriendsDrawer.Link.cs:144` records that limit. No safety or Scrolller code was changed.

| Command | Result tail |
| --- | --- |
| `dotnet-install --channel 10.0` | Installed 10.0.403; initial ci.dot.net discovery returned 403 before successful builds.dotnet.microsoft.com download |
| `dotnet build CCP.Core -t:Rebuild -m:1 -p:UseSharedCompilation=false` | 50 warnings, 0 errors |
| `dotnet build CCP.Avalonia -m:1 -p:UseSharedCompilation=false` | 201 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests -m:1 -p:UseSharedCompilation=false` | 0 errors |
| `dotnet test Tests/CCP.Core.Tests -m:1 -p:UseSharedCompilation=false` | Failed: 0, Passed: 4635, Skipped: 6, Total: 4641 |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~FriendsLinkPortTests\|FullyQualifiedName~FriendsDrawerTests\|FullyQualifiedName~TestIsolationTests'` | Failed: 0, Passed: 49, Skipped: 0 |
| `bash .github/scripts/core-guards.sh` | All Core guards passed |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK |
| `git diff --check` | Clean |

Fail-proof: saved the good drawer and link partial, restored the base drawer (removing
link entry points, token normalization and platform marks), and inverted the copy/new
service guard to disable those handlers. Ran `FriendsLinkPortTests`: Failed 7,
Passed 0, Skipped 0. Each of the seven tests failed at its missing control, operation,
claim, fallback or mark assertion. Restored both source files byte for byte before
rebuilding and rerunning the affected suite. No tests were weakened or newly skipped.

Validation retry: initial concurrent compiler writes left an incomplete Core DLL;
a sequential Core rebuild and dependent rebuild fixed discovery. Final results above
come from intact outputs. Headless rendering ran successfully; no font exception.

Review P2 fixed: removed the constructor-time New link availability gate
(`CCP.Avalonia/Views/Controls/FriendsDrawer.Link.cs:61`), matching WPF
`ConditioningControlPanel/Controls/Friends/FriendsDrawer.Link.cs:88-94`.
`FriendsLinkPortTests.cs:74` constructs the persistent page while signed out,
rebinds the same drawer on sign-in, asserts the same button is enabled, clicks it,
and checks revoke then link, copied URL and success text. Fail-proof before the fix:
Failed 1, Passed 0, Skipped 0, at the enabled assertion (`:92`). No new strings.

Review validation uses `DOTNET_CLI_UI_LANGUAGE=en`, .NET 10.0.403 and the same
`-m:1 -p:UseSharedCompilation=false` builds: Core, Avalonia and Avalonia.Tests each
returned 0 errors. The affected Avalonia filter above now passes 50, skips 0,
including the regression after its pre-fix failure. Core guards and docs self-check
pass, and the existing New link string is present in all ten language files.
Full Core review run: Failed 0, Passed 4635, Skipped 6, Total 4641 (existing skips).
