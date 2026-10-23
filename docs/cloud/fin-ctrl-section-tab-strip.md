# fin-ctrl-section-tab-strip

- Expose Studio's Just Drop pill in WPF table order and ask before opening the existing shop window. Copy the localized title/body/actions, hue, placement, focus, and window-vs-page selection behavior. Cancel, light dismissal, section rebuild/hide, detach and panic dismiss the card; a stale acceptance cannot launch after dismissal.
- Adapt the four-strip travelling lift from read-only `parity/trunk` commit `1eaffbafab94d59f997bb1ae622055b5cc06ff7b` (`CCP.Avalonia/Controls/NavRail/SectionEdgeLift.cs`). Keep this head's existing painters and shared `FrameClock`; preserve WPF's clockwise 12 s lap, 3 s legs, 24 fps, pause/resume and Reduced/Off/tier gates. No other trunk files were imported.
- Add the WPF breadcrumb change fade: 0.25 to 1 in 150 ms at Full, 75 ms at Reduced, instant at Off. Repeated unchanged crumbs do not restart it.
- Add focused tests for confirmation, keyboard focus vs activation, cancellation/panic, existing Companion/Premium routes, Settings language rebuild, real moved-note fade timer completion/replacement, breadcrumb fade and travelling-lift legs/lifecycle.
- Replace the stale ledger entry with one honest `stub` row and an explicit remaining list.

## Base and WPF audit

Base: `eef2c7e0bea2e4147bdf8889210a0b0d461fbd7f` (`origin/avalonia-port/cloud-base-wire-ctrl-section-tab-strip-b`). The original snapshot had a different commit and its fetch failed on a read-only filesystem; work used the requested independent fallback clone, pinned and checked clean before changes.

Read the row's WPF references at that SHA: `Controls/NavRail/SectionTabStrip.xaml`, `.xaml.cs`, `.Help.cs`, and `MainWindow/MainWindow.SectionChrome.cs`. Also read `MainWindow.SectionEdge.cs`, `Controls/NavRail/SectionEdgeRules.cs`, `EdgeParticles.cs`, and the supporting FX code.

| WPF behavior absent or unproven on the base | Finding and disposition |
| --- | --- |
| Just Drop pill and ask-first card (`Choose`, `AskBeforeOpening`) | The existing `OpenJustDrop` and `GameWindow.JustDrop` already reach the shop and enforce server availability. Only `CanOpenNavTab` hid the strip pill. Connected it and the confirmation; the game already registers with panic. Added a separate panic entry for the card. |
| Companion and Premium paths said to be absent | That ledger description was stale: all Companion keys and Premium already have `TabPanels` routes. Premium is intentionally hidden in WPF's `NavSections` table and Home draws no strip. Added route assertions. Personality/Permissions/Links still scroll visible room zones pending Companion V2; they are not dedicated WPF-equivalent pages yet. |
| Full-motion travelling lift | Implemented four clipped strips, clockwise top/right/bottom/left, parked before/after each leg. Window and motion/tier changes recheck the shared-clock gate without navigation. |
| Edge fog and embers | Still missing: four 56 px strips, Full fog + drift embers, Reduced half fog/half speed without embers, no particles at Off/low tier, hue crossfade and brightness gain, static band thinned from 28 to 16 px at 70% alpha while fog runs. This head's `AmbientFxCanvas` has neither EdgeFog nor EdgeDrift. The WPF fog math/simulation/renderer alone spans 590 lines before host, embers, integration and focused tests. Deferred to fit this unit's 1000-added-line ceiling. |
| Breadcrumb change crossfade (`Crossfade`) | Newly found during reference audit; implemented with a finite animation on the Visual, cancelling its predecessor and on detach. |
| Settings page word re-read after language rebuild | Implementation already called `SettingsPageLabel` in `Rebuild`; added a test that changes the language while Monitors remains checked. |
| Moved note hidden after its 300 ms fade | Existing callback already hid it. Added Full/Reduced tests that await the real fade timer and verify a replacement note cancels an older fade. |
| Section-hue depth fan-out (`PaintSectionWash`) | Still absent: `PaintDepthRail`, `PaintDepthHud`, `SettingsTab.PaintDepthHome`, `QuestsTab.PaintDepthQuests`. Their depth surfaces/painters need their owning rail, HUD, dashboard and quests lanes. The strip's own depth remains connected. |
| Cloud sync notice composed with the title (`SetSyncIssueTitle`) | Still absent: WPF `MainWindow.xaml.cs:743` calls it from `OnSyncHealthChanged`. This head has no equivalent shell sync-health handler. The existing navigation title remains connected; no unused setter was imported. |

The remaining gaps include live WPF effects, so P122/dead-caller promotion does not apply. The row remains `stub`, not `wired` or `verified`; this is not a dependency-only NOOP.

## Verification limits

All .NET commands used SDK 10.0.402, Release configuration; test commands set `DiffEngine_Disabled=true`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | `0 Error(s)` |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | `0 Error(s)` (existing warnings) |
| `dotnet build <test-project> -c Release`, every Linux test project listed in `.github/workflows/build.yml` | 0 each | CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests: each `0 Error(s)` |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | `512 Warning(s)`, `0 Error(s)` |
| `bash .github/scripts/core-guards.sh` | 0 | `All Core guards passed.` |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 0 | `Failed: 0, Passed: 2811, Skipped: 5, Total: 2816` |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~SectionEdgeLiftTests.ClockwiseLegWaitsCrossesAndParks` | 0 | `Failed: 0, Passed: 14, Skipped: 0, Total: 14` |
| `git diff --check` | 0 | no output |

The first multi-project build sweep exhausted the cloud disk while duplicating generated test outputs. It was rerun successfully one project at a time, removing only untracked generated binaries between builds. The first guard invocation also needed writable temporary storage; its successful run used that access.

The five Core skips are the existing conditional Vosk tests: the required model is unavailable here and its download is known to return 403. No tests were weakened, removed, or modified to skip.

Window/font-dependent tests were compiled, not run, under the task's explicit font-creation limitation: `JustDropWindowTests`, `SectionTabStripShellTests`, `SectionStripMovedNoteTests`, `SectionPaintShellTests`, and the window cases of `SectionEdgeLiftTests` need the supervisor's font-capable GitHub runner. The 14 pure lift cases above ran without creating an application/window. No valid before/after UI failure proof was obtained; the cancellation defect found during review has a compiled regression assertion, not an observed failing UI run.

No render-all, real desktop/window interaction, real web shop, or Windows test execution was run. Windows/WPF code was cross-compiled successfully and remains unchanged. Remaining parity gaps are listed in the audit and ledger, so the row stays `stub`.

## Assumptions

- Preserve the pinned base's live Companion room-zone routes until `views-companion-v2-conversation` supplies the dedicated pages, as its existing routing comment and ledger require.
- Draw Just Drop even while its server flag is closed, as WPF builds every visible table pill; the existing launch gate remains authoritative.
- Avalonia has no WPF `PopupAnimation.Fade`; use a 150 ms card opacity transition above Off and immediate display at Off. The WPF card does not halve its popup fade at Reduced.
- Close the new confirmation on panic, strip hide and detach so it cannot launch later from a stale click. The existing shop is already a `games` panic surface.
