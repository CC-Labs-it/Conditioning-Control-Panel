You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/main-20261015-compat`. Work on a branch named `avalonia-port/cloud-sync7-local-ai` created from it (if your environment names the branch itself, keep that name and put `sync7-local-ai` in the PR title).
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
**sync7-local-ai: local AI wizard: PC hardware warning, writes the verified host.** Port these WPF main commits (already merged into the WPF head on this base) into the Avalonia head / Core. Strings, `Assets/web` and the Core files these commits touched are already merged (see each row's note); you port the head half. Each row is in `docs/avalonia-main-sync-20261015.md` with status needs-port (`sync7-local-ai`).

- `798568154` (row 129) feat(local-ai): the setup wizard warns that the model runs on the player's PC, and checks memory + graphics memory
  - WPF: `LocalAiHardware.cs`, `LocalAiSetupWizard.xaml`, `LocalAiSetupWizard.xaml.cs`; langs: 9; tests: 1
  - counterpart: `Views/Dialogs/LocalAiSetupWizard.axaml`
  - ledger note: strings merged
- `3c2816fc5` (row 282) fix(local-ai): setup wizard writes the host it verified
  - WPF: `LocalAiSetupWizard.xaml.cs`, `MainWindow.Patreon.cs`; tests: 1; Core: 1
  - counterpart: Core `OllamaSetupService.cs` (merged); `Views/Dialogs/LocalAiSetupWizard.axaml`, `Views/Windows/MainShellWindow.Patreon.cs`
  - ledger note: Core half merged

Notes: `LocalAiHardware` is Registry + kernel32 (Windows-only): put the probe behind a small seam; non-Windows returns unknown and shows no false warning.

For every row, read the WPF diff (`git show <sha>`) and the WPF files as they are now on the base: the WPF code is the spec for layout, text, order, spacing, timing and behaviour. Port the behaviour into CCP.Avalonia so a real user path exists (reachable from the UI as in WPF) and matches WPF, with focused tests (Tests/CCP.Avalonia.Tests headless, or Tests/CCP.Core.Tests for rules you move into Core). Pure logic goes into CCP.Core (WPF then calls the Core copy; never two copies). Reuse existing Avalonia helpers/controls; do not invent triggers or hosts WPF does not have. Parts that target a host this head does not have (DtRH, Goon, Arcademy, Back Room) are n/a: say so in the row note. Panic must still close/stop anything you add that WPF's panic closes (register it in CCP.Avalonia PanicSurfaces as the other surfaces are, adding its sorted line to the test's SafetyCritical list).
If the work exceeds 1000 added lines, port the largest coherent set of rows, leave the rest `needs-port`, and name them under follow-up lane `sync7-local-ai-next` in their row notes.

Acceptance:
- In `docs/avalonia-main-sync-20261015.md`, tick each row you ported: status needs-port (`<its lane>`) -> ported (`sync7-local-ai`), notes gain the test names (and what is compiled-not-run). Update the matching `docs/avalonia-parity.md` row too if one applies (status/evidence only). Never mark any row "verified".
- Avalonia + Core tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/sync7-local-ai.md.
- Run `dotnet run --project CCP.Avalonia -- --render-all <dir>` if the change is visual and mention which PNG shows it.


## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/sync7-local-ai.md` in the branch: bullets of what changed (with WPF main commits and any trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.
