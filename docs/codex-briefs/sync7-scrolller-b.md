You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/main-20261015-compat`. Work on a branch named `avalonia-port/cloud-sync7-scrolller-b` created from it (if your environment names the branch itself, keep that name and put `sync7-scrolller-b` in the PR title).
- Change nothing outside this unit. If a PR is created from this task it must be a DRAFT whose base is `avalonia-port/main-20261015-compat`, never `main`. Never merge, never force-push.
- One unit per branch; Conventional-style commit messages.
- Line cap: at most 1000 ADDED lines vs the base (`git diff -M --numstat origin/avalonia-port/main-20261015-compat | awk '{a+=$1} END{print a}'`).

## Project rules (compact)
- Heads: CCP.Avalonia (Avalonia 12.1.x, net10.0, the shipped app), ConditioningControlPanel/ (WPF, frozen REFERENCE: read it for behaviour, text, order, timing; it includes release/7.1.14). Shared engine CCP.Core (net10.0).
- CCP.Core must never reference Avalonia, WPF or App.* (check: `bash .github/scripts/core-guards.sh`).
- Ponytail: smallest correct diff; reuse what Core/heads already have; no speculative abstractions, no interface with one implementation, no config for constant values; drop imported code nothing calls; `ponytail:` comment on deliberate shortcuts.
- Tests must prove behaviour: each new behaviour gets a focused test (Core tests or headless Tests/CCP.Avalonia.Tests); for fixes, show the test fails before the fix. Never weaken, skip or delete tests to get green.
- New icons: Microsoft Fluent UI System Icons (as the repo already uses), never emoji-as-icon.
- Icon guard: IconGuardTests ratchets emoji and icon-prefixed loc keys; use IconGlyph/IconLabel, never add emoji or icon-prefixed strings.
- Avalonia traps (must follow): (1) never Animation.RunAsync on a Transform (InvalidCastException) - use Helpers/TransformTween.cs if present or animate the Visual; (2) no infinite Animation / animated Effect in the main shell (forces 60 Hz composition) - ambient loops go on a shared clock; (3) no Effect (DropShadow/Blur) on tiles or under a looping FX layer - use a sibling Border BoxShadow; (4) clamp Opacity to 0..1 with overshooting easings; (5) use MaxLines, not MaxHeight, to cap text; (6) never hand a mutable SKBitmap across threads - publish an immutable SKImage. Also: Avalonia RenderTransformOrigin defaults to Center (WPF TopLeft).
- OS-specific code goes behind a small seam; Windows code compiles but cannot be run here - say so.
- Tests that open windows or notifications must never touch the real desktop or real user profile (tests use CCP_USERDATA_DIR sandboxes; the Avalonia SecretStore is memory-only there).
- Source for imports: `git fetch origin parity/trunk` (another dev's branch, READ ONLY). Import ADAPTED to our tree; where a file already exists on our base, our file wins: merge behaviour into it, never overwrite. Credit imported trunk commit hashes in the PR-body draft.

## Unit
**sync7-scrolller-b: licence switch, honest user-agent, device-cache routing for native media, For You back-off.** Port these WPF main commits (already merged into the WPF head on this base) into the Avalonia head / Core. Strings, `Assets/web` and the Core files these commits touched are already merged (see each row's note); you port the head half. Each row is in `docs/avalonia-main-sync-20261015.md` with status needs-port (`sync7-scrolller`).

- `9c3dca65e` (row 163) fix(online media): tell Scrolller who we are
  - WPF: `RemoteMediaCache.cs`; tests: 1; Core: 1
  - counterpart: Core `ScrolllerSource.cs` (merged); head: none yet
  - ledger note: Core half merged
- `a870695b0` (row 195) feat(scrolller): obey the server's licence switch (GET /config/scrolller)
  - WPF: `App.xaml.cs`, `RemoteMediaCache.cs`, `ScrolllerPolicyService.cs`, `VideoService.cs`; tests: 1; Core: 2
  - counterpart: Core `FypOnlineCoordinator.cs`, `ScrolllerSource.cs` (merged); `App.axaml`
  - ledger note: Core half merged
- `b8b1179e6` (row 196) fix(scrolller): review - policy read honours Offline mode; JustDrop token judged on the parsed url
  - WPF: `JustDropHostService.cs`, `ScrolllerPolicyService.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `69ec82e31` (row 197) fix(scrolller): remote clips streamed by LibVLC send the app's user-agent
  - WPF: `VideoService.cs`
  - counterpart: head: none yet
  - ledger note: head half
- `9c54f11ba` (row 199) feat(scrolller): daily allowance contract (ledger, limits from the switch, cache API)
  - WPF: `ScrolllerAllowance.cs`, `ScrolllerDiskCache.cs`, `ScrolllerItemIndex.cs`, `ScrolllerPolicy.cs`, `ScrolllerPolicyService.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `076bfe0be` (row 200) feat(scrolller): reuse mix - cached files fill session batches, labels on every mapped item
  - WPF: `ScrolllerReuse.cs`; tests: 1; Core: 2
  - counterpart: Core `FypOnlineCoordinator.cs`, `ScrolllerSource.cs` (merged); head: none yet
  - ledger note: Core half merged
- `4af799d30` (row 202) feat(scrolller): device cache for Scrolller media (persistent LRU, 2 GB)
  - WPF: `ScrolllerDiskCache.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `a25d6f9db` (row 203) feat(scrolller): route remote media and VLC clips through the device cache, away watcher
  - WPF: `App.xaml.cs`, `RemoteMediaCache.cs`, `ScrolllerAwayWatcher.cs`, `VideoService.cs`; tests: 2
  - counterpart: `App.axaml`
  - ledger note: head half
- `0252b20e6` (row 205) feat(scrolller): a free install's first day of online media gets 500 MB
  - WPF: `ScrolllerAllowance.cs`, `ScrolllerPolicyService.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `43edcea2d` (row 211) fix(scrolller): For You backs off when spent or away, one post per batch
  - WPF: `FypHostService.cs`, `ScrolllerReuse.cs`; tests: 1; web: 1; Core: 1
  - counterpart: Core `FypOnlineCoordinator.cs` (merged); head: none yet
  - ledger note: Core half merged; page half in Assets/web merged
- `05d830b12` (row 212) fix(scrolller): the share meter hears midnight, plan changes and every way the day gets spent
  - WPF: `ScrolllerAllowance.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `9e4cb5393` (row 213) fix(scrolller): the device cache loads in the background, keeps rows it cannot evict
  - WPF: `ScrolllerDiskCache.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `838bd8b5a` (row 215) fix(scrolller): a Scrolller clip never streams uncached; oversized bodies refused unread
  - WPF: `App.xaml.cs`, `RemoteMediaCache.cs`, `ScrolllerDiskCache.cs`, `VideoService.cs`; tests: 2
  - counterpart: `App.axaml`
  - ledger note: head half
- `0e9ea5d31` (row 237) fix(online-media): keep the daily share per account, not per PC
  - WPF: `MainWindow.OnlineMeter.cs`, `ScrolllerAllowance.cs`, `ScrolllerPolicyService.cs`; tests: 2
  - counterpart: head: none yet
  - ledger note: head half
- `5c93bbfeb` (row 242) fix(online-media): keep Basic 2048 MB and Prime 6144 MB (owner change of course)
  - WPF: `ScrolllerAllowance.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half
- `b2ee03745` (row 250) fix(online-media): the device cache keeps only bodies that are the media their url names
  - WPF: `ScrolllerDiskCache.cs`
  - counterpart: head: none yet
  - ledger note: head half
- `67cd73adf` (row 255) fix(online-media): the startup check drops only on positive evidence
  - WPF: `ScrolllerDiskCache.cs`
  - counterpart: head: none yet
  - ledger note: head half
- `82b098432` (row 263) fix(online-media): no fresh fetch while the share's book is locked
  - WPF: `ScrolllerAllowance.cs`, `ScrolllerPolicyService.cs`; tests: 1
  - counterpart: head: none yet
  - ledger note: head half

Notes: Ten of these rows are Core-only on this base (the Scrolller cluster `ScrolllerPolicy*`, `ScrolllerDiskCache`, `ScrolllerAllowance`, `ScrolllerReuse`, `ScrolllerItemIndex` moved to CCP.Core in -compat and the WPF tests cover it). For those, the port is the Avalonia head path that reaches the Core code (startup policy read, remote media and LibVLC clips through the device cache, away watcher): tick them with the Avalonia/Core test that proves the head uses it. Do not duplicate the WPF tests.

For every row, read the WPF diff (`git show <sha>`) and the WPF files as they are now on the base: the WPF code is the spec for layout, text, order, spacing, timing and behaviour. Port the behaviour into CCP.Avalonia so a real user path exists (reachable from the UI as in WPF) and matches WPF, with focused tests (Tests/CCP.Avalonia.Tests headless, or Tests/CCP.Core.Tests for rules you move into Core). Pure logic goes into CCP.Core (WPF then calls the Core copy; never two copies). Reuse existing Avalonia helpers/controls; do not invent triggers or hosts WPF does not have. Parts that target a host this head does not have (DtRH, Goon, Arcademy, Back Room) are n/a: say so in the row note. Panic must still close/stop anything you add that WPF's panic closes (register it in CCP.Avalonia PanicSurfaces as the other surfaces are, adding its sorted line to the test's SafetyCritical list).
If the work exceeds 1000 added lines, port the largest coherent set of rows, leave the rest `needs-port`, and name them under follow-up lane `sync7-scrolller-b-next` in their row notes.

Acceptance:
- In `docs/avalonia-main-sync-20261015.md`, tick each row you ported: status needs-port (`<its lane>`) -> ported (`sync7-scrolller-b`), notes gain the test names (and what is compiled-not-run). Update the matching `docs/avalonia-parity.md` row too if one applies (status/evidence only). Never mark any row "verified".
- Avalonia + Core tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/sync7-scrolller-b.md.
- Run `dotnet run --project CCP.Avalonia -- --render-all <dir>` if the change is visual and mention which PNG shows it.


## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/sync7-scrolller-b.md` in the branch: bullets of what changed (with WPF main commits and any trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.
