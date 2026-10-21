You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/cloud-wire-win-emi-ring` (current stack tip). Work on a branch named `avalonia-port/cloud-wire-win-emi-ring-b` created from it.
- Change nothing outside this unit. If a PR is created it must be a DRAFT whose base is `avalonia-port/cloud-wire-win-emi-ring`, never `main`. Never merge, never force-push.
- One unit per branch; Conventional-style commit messages.
- Line cap: at most 1000 ADDED lines vs the base (`git diff -M --numstat origin/avalonia-port/cloud-wire-win-emi-ring | awk '{a+=$1} END{print a}'`).

## Project rules (compact)
- Heads: CCP.Avalonia (Avalonia 12.1.x, net10.0, the shipped app), ConditioningControlPanel/ (WPF, frozen REFERENCE: read it for behaviour, text, order, timing; it includes release/7.1.5). Shared engine CCP.Core (net10.0).
- CCP.Core must never reference Avalonia, WPF or App.* (check: `bash .github/scripts/core-guards.sh`).
- Ponytail: smallest correct diff; reuse what Core/heads already have; no speculative abstractions, no interface with one implementation, no config for constant values; drop imported code nothing calls; `ponytail:` comment on deliberate shortcuts.
- Tests must prove behaviour: each new behaviour gets a focused test (Core tests or headless Tests/CCP.Avalonia.Tests); for fixes, show the test fails before the fix. Never weaken, skip or delete tests to get green.
- New icons: Microsoft Fluent UI System Icons (as the repo already uses), never emoji-as-icon.
- Avalonia traps (must follow): (1) never Animation.RunAsync on a Transform (InvalidCastException) - use Helpers/TransformTween.cs if present or animate the Visual; (2) no infinite Animation / animated Effect in the main shell (forces 60 Hz composition) - ambient loops go on a shared clock; (3) no Effect (DropShadow/Blur) on tiles or under a looping FX layer - use a sibling Border BoxShadow; (4) clamp Opacity to 0..1 with overshooting easings; (5) use MaxLines, not MaxHeight, to cap text; (6) never hand a mutable SKBitmap across threads - publish an immutable SKImage. Also: Avalonia RenderTransformOrigin defaults to Center (WPF TopLeft).
- OS-specific code goes behind a small seam; Windows code compiles but cannot be run here - say so.
- Tests that open windows or notifications must never touch the real desktop or real user profile (tests use CCP_USERDATA_DIR sandboxes; the Avalonia SecretStore is memory-only there).
- Source for imports: `git fetch origin parity/trunk` (another dev's branch, READ ONLY). Import ADAPTED to our tree; where a file already exists on our base, our file wins: merge behaviour into it, never overwrite. Credit imported trunk commit hashes in the PR-body draft.

## Unit
**wire-win-emi-ring-b (follow-up, ledger row `win-emi-ring`).** Your base branch `avalonia-port/cloud-wire-win-emi-ring` already contains the first slice for row `win-emi-ring`. In `docs/avalonia-parity.md` on that base, read row `win-emi-ring`: its notes name what is still missing under follow-up lane `wire-win-emi-ring-b`. Port exactly that remaining WPF behaviour (WPF reference files are cited in the row) so the row can move to **wired**: a real user path as in WPF, behaviour matching WPF, focused tests (headless Avalonia or Core). Pure logic in CCP.Core (WPF calls the Core copy; never two copies). Do not invent triggers or hosts WPF lacks. Panic must close/stop anything new as WPF does (CCP.Avalonia PanicSurfaces + its sorted SafetyCritical test line).
If it still does not fit 1000 added lines, port the largest coherent slice, keep the row "stub" and name the rest as `wire-win-emi-ring-c`. Update ONLY row `win-emi-ring`. Never mark any row "verified".
Acceptance: touched Core/Avalonia tests pass, core-guards pass, branch pushed with docs/cloud/wire-win-emi-ring-b.md.

## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/wire-win-emi-ring-b.md` in the branch: bullets of what changed (with trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the pushed branch name and head commit.
