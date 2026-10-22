You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/goongame-core-d` (current stack tip). Work on a branch named `avalonia-port/cloud-companion-picker-host` created from it (if your environment names the branch itself, keep that name and put `companion-picker-host` in the PR title).
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
**companion-picker-host (stub -> wired, ledger row `views-companion-picker-card`).** In `docs/avalonia-parity.md` the row `views-companion-picker-card` is "stub". Read the whole row, then read the WPF reference: it is the spec for layout, text and order. The row says what is left: "card ported; hosting in ModManagerDialog changes the verified release-content-mod-manager-rows render -> lane companion-picker-host (needs a WPF reference render). ModManagerDialog (WPF ModManagerDialog.xaml:190) is its only host on this head (no Avalonia PersonalityPage); the host lane also sets `MorePersonalityOptions` (WPF PersonalityStudio, v2) and drops the dialog's old "Companion:" line and Tube Fit button as WPF did." and "Not ported (stub): the 160 ms portrait fade-in (WPF CompanionPickerCard), the `modmgr_avatar_single_hint` (a single disabled set shows no hint), and restarting the XP-drain timer when the perk changes (`PerkPicker.cs` ponytail)."
WPF references (line numbers at the base): `ConditioningControlPanel/Dialogs/ModManagerDialog.xaml:191` (`<cmp:CompanionPickerCard x:Name="CompanionCard" Margin="0,0,0,14"/>`), `ConditioningControlPanel/Dialogs/ModManagerDialog.xaml.cs:41-44` (v2 only: `CompanionCard.MorePersonalityOptions = PersonalityStudio.Show`), `ConditioningControlPanel/Views/Controls/Companion/CompanionPickerCard.xaml.cs:371` (`MorePersonalityOptions`), and the card's portrait fade / single-set hint in `CompanionPickerCard.xaml(.cs)`. Avalonia today: `CCP.Avalonia/Views/Dialogs/ModManagerDialog.axaml` still has the old "Companion Info" block (`TxtCompanion` :253-256) and `BtnTubeFit` (:276).
Host the ported card in Avalonia `ModManagerDialog` where WPF has it, remove `TxtCompanion` and `BtnTubeFit` (and their now-dead code-behind) as WPF did, and port the three "Not ported" items if they fit. `MorePersonalityOptions`: this head has no PersonalityStudio; leave it null (link hidden, as the card already supports) and say so in the row - do not port the studio. Ledger notes can be stale: first check the base for each item.
Render caveat: the row `release-content-mod-manager-rows` is "verified" and its render changes. Do not edit that row's status. Take `--render-view ModManagerDialog` (and `CompanionPickerCard`) after the change and name the PNGs in the PR body; a WPF reference render cannot be taken here - state that it is needed from a Windows machine before review.
Tests: headless `Tests/CCP.Avalonia.Tests` - the dialog hosts the card (found by name, owner chain reaches the shell so the personality combo writes through), the old Companion line and Tube Fit button are gone; plus focused tests for any ported "Not ported" item (fade-in duration, single-set hint text, perk change restarts the drain timer). Show at least one fails-before-fix proof.
If the work exceeds 1000 added lines, land the hosting first, keep the row "stub", and list what is left under follow-up lane `companion-picker-host-b`. Update ONLY row `views-companion-picker-card` in the ledger (status + evidence). Never mark any row "verified".
Acceptance: Avalonia tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/companion-picker-host.md.

## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/companion-picker-host.md` in the branch: bullets of what changed (with trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.

## Reuse
- `CCP.Avalonia/Views/Controls/Companion/CompanionPickerCard.axaml(.cs)` and `PerkPicker.cs` (predecessor lane companion-picker-card): the whole live/preview face, `MorePersonalityOptions` (:297, null hides the link), owner-chain shell lookup. Host it as is; do not fork it.
- `Tests/CCP.Avalonia.Tests/CompanionPickerCardTests.cs`: the shell-owned-window harness to reuse for the dialog test.
- Core `PersonalitySamples`, `CompanionPerks`, `CompanionPreview`; `AvatarTubeWindow.AvatarSetTitle(set)` for the Look label.
