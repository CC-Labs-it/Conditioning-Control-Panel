# Friends drawer link feedback

- Adapted WPF reference commits `018075f1c2ca4ca898406cfa110097b43e252cf0` and `5d7c2110a412011d929a465d29534a9d5de8d1f0`; fetched `parity/trunk` read-only. No files imported wholesale and no sibling changes re-ported.
- Added the always-visible Copy link pill next to the code, code fallback, clipboard-failure feedback, and New link inside Add friend. Pasted links and bare tokens claim directly; code entry keeps its existing route.
- Added the four-second note hold, gentle-visual-aware 180 ms rise / 260 ms fade, replacement-pill pop, keystroke-aware normalization, and single service-owned claim refresh.
- Reused Core and all existing translations. No new window, popup, overlay or game surface; these are children of the existing drawer, so no new PanicSurfaces entry is needed.

## Commit coverage

- `018075f1c`: **ported** — `CCP.Avalonia/Views/Controls/FriendsDrawer.Link.cs:44` (Copy link, note, renewal and claim), `CCP.Avalonia/Views/Controls/FriendsDrawer.cs:98` (token entry), `:602` (pill beside code), `:614` (claim dispatch). Tests: `LinkCopyFetchesEveryPressAndNewLinkRevokesBeforeCopying`, `LinkCopyFallsBackToCodeAndClipboardFailureSaysTryLater`, `PastedLinkClaimsAndOnlyTheServiceRefreshes` in `Tests/CCP.Avalonia.Tests/FriendsDrawerLinkTests.cs`.
- `5d7c2110a`: **ported** drawer portion — `FriendsDrawer.Link.cs:89` (replacement pill), `:140` (no duplicate refresh), `:147` (note timing and generation guard); `FriendsDrawer.cs:105` (one-keystroke normalization). Tests above plus `HandTypedCcpTokenSurvivesAndPastedCodeKeepsItsPrefix`.
- `5d7c2110a`: **already-covered** Core portion — `CCP.Core/Services/Friends/FriendsModel.cs:83` returns cached platform subsets; `CCP.Core/Services/Friends/FriendsService.Link.cs:18` asks the server on every press and retains the offline cache; `CCP.Core/Services/Friends/FriendsLinkRules.cs:95` accepts the keystroke flag. No Core changes made.
- **blocked-by-host detail for both commits:** FriendsSfx and the drawer's shared sparks/shockwave FX layer do not exist in this head (the existing Pickers partial documents the same limitation). No audio or particle host was invented. The local Copy link scale feedback uses the existing shared BeatLoop for a finite 380 ms pop; full WPF elastic/sparks feedback remains limited by that missing host.

## Verification limits

- `dotnet build <project> -c Release -m:1`: CCP.Core, CCP.Avalonia and every test project named by `.github/workflows/build.yml` compiled with zero errors (15 projects total). The WPF test project additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled but not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,953 passed, 0 failed, 5 existing Vosk tests skipped because the model is unavailable. No tests were changed or skipped to obtain this result.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release -m:1 --filter 'FullyQualifiedName~FriendsDrawerTests'`: exit 0; 36 passed, 0 failed, 0 skipped. New tests initialize bundled fonts on the headless platform, use the existing sandbox profile, fake HTTP, and replace clipboard writes; they never touch a desktop clipboard.
- Negative proof: temporarily replaced claim dispatch with `ParseToken(null)` and the one-keystroke argument with `false`, then ran the claim/token filter: exit 1, all 7 cases failed. Restored both production lines and reran the full drawer class: all 36 passed. The replacement-pill test also failed before disabling the theme's transform transition, then passed with the finite shared-beat pop; it checks actual growth, settling and clock removal.
- `git diff --check`: exit 0. `.github/scripts/test-plan-coverage.sh` is absent on this base (invocation exited 127); numbered manual steps were still added.
- No Windows desktop execution, real service/clipboard integration, visual comparison against WPF, or unrelated Avalonia test suites were run. Missing sound/particle hosts and the simplified non-elastic scale return remain verification/parity limits.

## Assumptions

- Preserve this head's established keyboard-accessible button treatment for inline links.
- Preserve the existing drawer's missing sound/particle-host boundary and the current Core behavior, including sibling changes already merged there.
- Keep WPF copy from the two listed commits, rather than importing later link-note wording.
