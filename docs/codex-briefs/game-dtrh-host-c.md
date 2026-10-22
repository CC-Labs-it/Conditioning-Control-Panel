You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/goongame-core-d` (current stack tip). Work on a branch named `avalonia-port/cloud-game-dtrh-host-c` created from it (if your environment names the branch itself, keep that name and put `game-dtrh-host-c` in the PR title).
- Change nothing outside this unit. If a PR is created from this task it must be a DRAFT whose base is `avalonia-port/goongame-core-d`, never `main`. Never merge, never force-push.
- One unit per branch; Conventional-style commit messages.
- Line cap: at most 1000 ADDED lines vs the base (`git diff -M --numstat origin/avalonia-port/goongame-core-d | awk '{a+=$1} END{print a}'`).

## Project rules (compact)
- Heads: CCP.Avalonia (Avalonia 12.1.x, net10.0, the shipped app), ConditioningControlPanel/ (WPF, frozen REFERENCE: read it for behaviour, text, order, timing; it includes release/7.1.5). Shared engine CCP.Core (net10.0).
- CCP.Core must never reference Avalonia, WPF or App.* (check: `bash .github/scripts/core-guards.sh`).
- Ponytail: smallest correct diff; reuse what Core/heads already have; no speculative abstractions, no interface with one implementation, no config for constant values; drop imported code nothing calls; `ponytail:` comment on deliberate shortcuts.
- Tests must prove behaviour: each new behaviour gets a focused test (Core tests or headless Tests/CCP.Avalonia.Tests); for fixes, show the test fails before the fix. Never weaken, skip or delete tests to get green.
- New icons: Microsoft Fluent UI System Icons (as the repo already uses), never emoji-as-icon.
- Avalonia traps (must follow): (1) never Animation.RunAsync on a Transform (InvalidCastException) - use Helpers/TransformTween.cs if present or animate the Visual; (2) no infinite Animation / animated Effect in the main shell (forces 60 Hz composition) - ambient loops go on a shared clock; (3) no Effect (DropShadow/Blur) on tiles or under a looping FX layer - use a sibling Border BoxShadow; (4) clamp Opacity to 0..1 with overshooting easings; (5) use MaxLines, not MaxHeight, to cap text; (6) never hand a mutable SKBitmap across threads - publish an immutable SKImage. Also: Avalonia RenderTransformOrigin defaults to Center (WPF TopLeft).
- Icon guard: IconGuardTests ratchets emoji and icon-prefixed loc keys; use IconGlyph/IconLabel, never add emoji or icon-prefixed strings.
- OS-specific code goes behind a small seam; Windows code compiles but cannot be run here - say so.
- Tests that open windows or notifications must never touch the real desktop or real user profile (tests use CCP_USERDATA_DIR sandboxes; the Avalonia SecretStore is memory-only there).
- Source for imports: `git fetch origin parity/trunk` (another dev's branch, READ ONLY). Import ADAPTED to our tree; where a file already exists on our base, our file wins: merge behaviour into it, never overwrite. Credit imported trunk commit hashes in the PR-body draft.

## Unit
**game-dtrh-host-c (stub, ledger row `game-dtrh`).** In `docs/avalonia-parity.md` the row `game-dtrh` is "stub". Read the whole row (WPF reference files, Avalonia counterparts, the game-dtrh-host and game-dtrh-host-b notes, "Known deviations"), then read the WPF reference: it is the spec for messages, numbers, order and timing. The row says what is left: "Still missing (lane game-dtrh-host-c): the meta bridge (meta snapshot / meta-command, Warren shelves), the ccp.art / ccp.spirals / ccp.content / ccp.mod origins (bubble art, loom gifs, audio pack, mod content), loom-list/loom-*, favorites/asset-stats, heartbeat watchdog + relaunch-once, barks, sfx, fire-payload, freeze/mute, vn-speaking, haptics, fullscreen-set, report-bug, session stats, bubble credit, RevealService.Sync, crash sentinel, EMI desk fires, audio-web pack request, tucking the panel to the tray, SeasonRecap TrackFeature, the Play-tab/Exclusives doors, and kept-escape for race/board/Breakout."
WPF references (line numbers at the base): `ConditioningControlPanel/Services/Chaos/DtrhHostService.cs` - virtual-host origins ccp.art/ccp.spirals/ccp.mod :164-193, heartbeat start :225 / watch :948-977 / process-failed + `Recover` relaunch-once :979-1004, tray tuck `MinimizeToTrayForChaos` :233 (restore :1084), `OnPageMessage` switch :325-455 (`vn-speaking` :329, `sfx` :332, `fire-payload` :343 -> `FirePayload` :556, `freeze-state` :346 -> `ApplyWorldFreeze` :803, `mute-state` :349 -> `ApplyDiveMute` :786, `meta-command` :352, `run-started` SeasonRecap `TrackFeature` :369 + `ChaosCrashSentinel.Mark` :376, `bark` :386 -> `RouteBark` :718-770, `haptic-state` :393, `heartbeat` :397, `asset-stats` :400, `loom-save/delete/reveal` :405-433, `report-bug` :438 -> `OpenBugReport` :510, `fullscreen-set` :441 -> `ApplyHostFullscreen` :491), `PostLoomList` :464, `OnRunEnded` RevealService.Sync :646 / sentinel clear :663 / `DtrhSessionStatsStore.Record` :684, video credit hooks :833-910; `DtrhMetaBridge.cs` (571 lines, meta snapshot / meta-command); `DtrhModContent.cs` (313 lines, ccp.mod content); `DtrhNativePayloadRule.cs` (fire-payload rule).
This list is far larger than one unit. Pick the largest coherent slice that fits in 1000 added lines, in this preference order unless the base shows one is already done: (1) heartbeat watchdog + relaunch-once and the crash sentinel (robustness), (2) bark routing (`RouteBark`, through the existing Avalonia bark head) + sfx + freeze/mute + fire-payload, (3) the remaining origins/loom/meta bridge. Pure rules (message -> action mapping, watchdog timing, payload rule) go into CCP.Core (git mv the WPF rule files to Core where they are head-neutral, as game-dtrh-host did, and make WPF call them: one copy). Do not invent messages the page does not send. Panic must still close the window as today (`GameWindow.CloseAllForPanic`); anything you add (watchdog timers, relaunch) must stand down on panic and must not relaunch after a panic close. Ledger notes can be stale: first check the base for each item; if one is already ported, say so in the row instead of porting it again. Skip anything that is sync7-scrolller's (copy sweeper) and anything Windows-only (WebView2 process-failed kinds map to the WebKitGTK carrier's equivalent; if there is none, say so).
Keep the row "stub" unless every item is done, and list exactly what is left under a named follow-up lane `game-dtrh-host-d` in the row. Update ONLY row `game-dtrh` in the ledger (evidence: test names, what is compiled-not-run). Never mark any row "verified". Not visual unless you touch fullscreen; no render needed.
Tests: `Tests/CCP.Avalonia.Tests/GameWindowTests.cs` (`Dtrh_*` pattern: fake page messages through the carrier) and Core tests for moved rules; show at least one fails-before-fix proof.
Acceptance: Avalonia + Core tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/game-dtrh-host-c.md.

## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/game-dtrh-host-c.md` in the branch: bullets of what changed (with trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.

## Reuse
- `CCP.Avalonia/Views/Games/GameWindow.Dtrh.cs` (predecessors game-dtrh-host, game-dtrh-host-b): the message switch, ready -> init, manifest post, run lifecycle; add cases there.
- Core `CCP.Core/Services/Chaos/DtrhRunWire.cs`, `DtrhRunPayoutRule.cs`, `DtrhRunCloseRule.cs`, `DtrhRemoteMedia.cs`, `GameMediaManifest.cs`, `DtrhMotionWire.cs`: extend, do not add parallel types.
- `WebAssetServer` (+ `WebAssetServer.AssetPath`) serves the page and the `.temp/ccp_temp_remote_*` copies: new origins (ccp.art, ccp.spirals, ccp.mod) should be served paths there, not new servers.
- `CCP.Core/Services/Safety/PanicPolicy.cs` + `MainShellWindow.PanicKey.cs` (`TryGamePauseOnEscape`, KeptEscapePage) for any kept-escape extension to race/board/Breakout.
- `CCP.Avalonia/Platform/BarkHead.cs` + Core `BarkEngine` for routed barks (if CoreBark has the chaos notify hooks; otherwise note the gap).
- Tests: `GameWindowTests.Dtrh_*`, `DtrhRunWireTests`, `DtrhRemoteMediaTests` show the harness.
