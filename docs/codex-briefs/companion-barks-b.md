You are implementing ONE unit of the CCP Avalonia port in the GitHub repo CC-Labs-it/Conditioning-Control-Panel.

## Branch rules (hard)
- Base: `origin/avalonia-port/lock-title` (current stack tip). Work on a branch named `avalonia-port/cloud-companion-barks-b` created from it (if your environment names the branch itself, keep that name and put `companion-barks-b` in the PR title).
- Change nothing outside this unit. If a PR is created from this task it must be a DRAFT whose base is `avalonia-port/lock-title`, never `main`. Never merge, never force-push.
- One unit per branch; Conventional-style commit messages.
- Line cap: at most 1000 ADDED lines vs the base (`git diff -M --numstat origin/avalonia-port/lock-title | awk '{a+=$1} END{print a}'`).

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
**companion-barks-b (stub -> wired, ledger row `feat-companion-barks`).** In `docs/avalonia-parity.md` the row `feat-companion-barks` is "stub". Read the whole row (its WPF reference file, its Avalonia counterparts and every "Missing"/"Follow-up" item in its notes), then read the WPF reference: it is the spec for rules, numbers, order and timing. The row says what is left: "Follow-up lane companion-barks-b (port onto this engine; a second engine from companion-barks was dropped in the main-sync #7 merge, docs/avalonia-decisions.md 2026-10-22): idle chatter `DispatchIdle` with pool-wide no-repeat and the 0.35 gated-idle bias (BarkService.cs:165-171, :1178-1230); whisper floor (:1689, possession/emergency-exit attributions exempt :1690) and Chaos-narrator floor (:1695); tier latch key `id@Tier` (:1781); `DryRun` (:184, :200); an ordinary VOICED bark queues through `Giggle(line, phraseAudioPath, barkVoice)` (Speak :1943-1990) where BarkHead sends it to GigglePriority." WPF reference: `ConditioningControlPanel/Services/Companion/BarkService.cs`. Keep to these engine rules and the voiced-bark route; the ~30 unported WireSubscriptions sources (:660-925) are not this unit unless one is needed as the first caller of `DispatchIdle`. Do not re-add a second bark engine and do not touch WPF's own BarkService copy (pending decision 2026-10-12; that is lane barks-wpf-b).
Port the remaining behaviour into Core `BarkEngine` and the Avalonia head so the row becomes **wired**: a real path exists (idle chatter fires from the running app as in WPF), the behaviour matches WPF, and focused tests prove it (Tests/CCP.Core.Tests/BarkEngineTests.cs for rules, Tests/CCP.Avalonia.Tests/CompanionBarkHeadTests.cs headless for the head). Pure logic goes into CCP.Core. Reuse existing Avalonia helpers; do not invent triggers WPF does not have. Panic must still silence anything you add that WPF's panic silences (the 6 s safety hold already exists in BarkEngine; idle chatter must respect it). Ledger notes can be stale: first check the base for each item; if one is already ported, say so in the row instead of porting it again.
If the remaining work exceeds 1000 added lines, port the largest coherent slice, keep the row "stub", and list exactly what is left under a named follow-up lane `companion-barks-c` in the row. Update ONLY row `feat-companion-barks` in the ledger (status + evidence: test names, what is compiled-not-run). Never mark any row "verified". This change is not visual; no render needed.
Acceptance: Avalonia + Core tests for the touched classes pass, core-guards pass, branch pushed with docs/cloud/companion-barks-b.md.

## Environment
The setup script installed .NET 10 into ~/.dotnet (`export PATH=$HOME/.dotnet:$PATH`). Network may be off: work offline after restore. Headless Avalonia tests need no display. If a build or test cannot run here, say so; never claim results you did not see.

## Required before pushing
- `dotnet build` of CCP.Core, CCP.Avalonia and every test csproj listed in .github/workflows/build.yml that builds on Linux (the WPF projects cannot build here; build the WPF test project only if it builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- `bash .github/scripts/core-guards.sh`
- `dotnet test Tests/CCP.Core.Tests` and the relevant Tests/CCP.Avalonia.Tests classes (`--filter`), with `export DiffEngine_Disabled=true`.
- Write a PR-body draft to `docs/cloud/companion-barks-b.md` in the branch: bullets of what changed (with trunk commits credited), then `## Verification limits` (tests run + results, what was compiled but not run, e.g. Windows code).

## Final message
Changed files; commands run and their result tails; added-line count vs base; anything not run; the branch name and head commit. Never report test pass counts you did not see in this session.

## Reuse
- `CCP.Core/Services/Bark/BarkEngine.cs` is canonical (Raise/Speak, gate `EvaluateGate`, rotation, `SafetyHoldMs`, `GlobalGapExemptTriggers`); its header `ponytail:` note names DispatchIdle, CCP_BARK_DRYRUN, the whisper/Chaos-narrator gates and tier latches as waiting for a caller: land them there, remove that note's covered parts.
- `CCP.Core/Services/Bark/BarkRuleManifest.cs`, `BarkRuleSet.cs`, `BarkRule.cs`, `BarkVariant.cs`, `BarkContext.cs` and `CCP.Core/CoreBark.cs` (head-neutral event hooks) already exist; extend them, do not add parallel types.
- `CCP.Avalonia/Platform/BarkHead.cs` hosts the engine and owns the mouth: `Speak` (:90) already routes priority/voiced barks to `AvatarTubeWindow.GigglePriority`; its `ponytail:` note (:88) is the voiced-bark item to resolve.
