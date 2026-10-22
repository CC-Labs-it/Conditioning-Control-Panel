You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/goongame-core-d` (current stack tip). Work on a branch named `avalonia-port/cloud-chaos-unlock-card-b` created from it (if your environment names the branch itself, keep that name and put `chaos-unlock-card-b` in the PR title).
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
**chaos-unlock-card-b (stub -> wired, ledger row `chaos-unlock-card`).** In `docs/avalonia-parity.md` the row `chaos-unlock-card` is "stub". Read the whole row (its WPF reference file, its Avalonia counterparts and every "Missing"/"Still missing" item in its notes), then read the WPF reference: it is the spec for rules, numbers, order and timing. The row says what is left: "Still missing: the interactive mid-run overlay itself (scrim, click to continue, 12 s auto-dismiss, queue, `CloseActive` on panic/teardown) and the lesson-complete field pause it needs in `ChaosRunHost` - deferred to lane chaos-unlock-card-b; real item art (this head's ChaosArt has no sprites, glyph tile always shows)."
WPF references (line numbers at the base): `ConditioningControlPanel/Chaos/ChaosUnlockCardOverlay.cs` - overlay class :199, `AUTO_DISMISS_MS = 12000` :203, queue + `IsShowing` :205-210, `Show(data, onDismissed, autoDismiss)` :216, `CloseActive` :233, `ShowNext` :238, scrim hit-test + click dismiss :280-283, auto timer :291, `Display`/`DisplayCore` :295-301, `Dismiss` (fade, next card) :352-374, `CloseNow` :379 (`ApplyExStyles` :388-405 is Win32 no-activate styling: map to Avalonia `ShowActivated=false`/topmost, no P/Invoke). Caller: `ConditioningControlPanel/Services/Chaos/ChaosModeService.cs` - subscribe `ChaosLessons.LessonCompleted` :128, `OnLessonCompleted` :146-183 (hold the card while the draft owns the field, `claimPause` freezes clock/spawns/bubbles + input lock, `autoDismiss: !claimPause && !_lessonCardPaused`), `ResumeAfterLessonCard` :189-200+, the post-draft hand-off of `_pendingLessonCards` :1600-1613, `CloseActive` in teardown/panic :3175 and :3228.
Port the overlay into the Avalonia head (reuse `ChaosUnlockCardLayer`'s card visual, do not rebuild it) and wire it into `ChaosRunHost` so the row becomes **wired**: a lesson completed mid-run shows the card over the field, pauses the run as WPF does, click (or 12 s on a pause-free card) dismisses, queued cards follow, the run resumes after the last one, and panic/teardown closes it. Pure decisions (claim-pause / auto-dismiss / hold-for-draft / resume-when-queue-empty) go into CCP.Core if they are not trivial; reuse Core `ChaosLessons.LessonCompleted` (CCP.Core/Services/Chaos/ChaosLessons.cs:118). Panic must silence it (`ChaosRunHost.ForceShutdown` gets a `ChaosUnlockCardOverlay.CloseActive()` like its siblings). Real item art stays out (no ChaosArt sprites on this head): keep the glyph tile and say so in the row. Ledger notes can be stale: first check the base for each item; if one is already ported, say so in the row instead of porting it again.
Tests: headless `Tests/CCP.Avalonia.Tests` (extend `ChaosUnlockCardLayerTests.cs` or add `ChaosUnlockCardOverlayTests.cs`): queue order, click dismiss, auto-dismiss only when not pausing, pause held until the queue is empty, ForceShutdown closes and clears the queue; Core tests for any rule you extract. Show at least one fails-before-fix proof.
If the remaining work exceeds 1000 added lines, port the largest coherent slice, keep the row "stub", and list exactly what is left under a named follow-up lane `chaos-unlock-card-c` in the row. Update ONLY row `chaos-unlock-card` in the ledger (status + evidence: test names, what is compiled-not-run). Never mark any row "verified". The overlay is visual: if the repo has a `--render-view` hook for overlays, add a render and name it in the PR body; otherwise say no render was taken.
Acceptance: Avalonia + Core tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/chaos-unlock-card-b.md.

## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/chaos-unlock-card-b.md` in the branch: bullets of what changed (with trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.

## Reuse
- `CCP.Avalonia/Views/Chaos/ChaosUnlockCardLayer.cs` (predecessor lane chaos-unlock-card): the ported `BuildCardVisual` (ribbon, glyph tile, title/desc/flavor/context, accent glow flare, icon pop, reward cue). Host it in the overlay; do not duplicate.
- `CCP.Core/Services/Chaos/ChaosUnlockCardRules.cs` (`ForLesson`, `ForBoonUnlock`, `ForCapstone`, `ForHabit`, `ForPocket`) - the card data; `Tests/CCP.Core.Tests/ChaosUnlockCardRulesTests.cs`.
- `CCP.Avalonia/Views/Chaos/ChaosAnnouncerOverlay.cs`: the pattern for a one-window, kept-alive, queue-driven chaos overlay with a static `CloseActive` called from `ChaosRunHost.ForceShutdown`.
- `CCP.Avalonia/Views/Chaos/ChaosRunHost.cs`: `_manualPaused` / `ToggleManualPause` (:286) and `BubbleOverlay.HoldChaos(...)` already do WPF's freeze + input lock; reuse them for the lesson pause instead of new freeze plumbing. `ForceShutdown` (:302) is where `CloseActive` goes.
