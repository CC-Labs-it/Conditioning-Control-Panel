# Finish shell-quest-stamps

- Finish the header's remaining WPF motion: a transition-only 420 ms completion pop (0.7 -> 1.35 at 45% with BackEase amplitude 0.6 -> 1 with cubic ease-out), plus a 0.9 -> 0 gold flash removed after 650 ms. First paint of an already completed seat does not celebrate.
- Match stamp hover (170 ms in / 200 ms out, cubic), host lift (150 ms, quadratic), popup opacity (150 ms, linear), and popup slide (-7 -> 0 over 180 ms, cubic). Crossing stamps updates the reused card without replaying its entrance. Motion Off snaps to the final pose.
- Use the existing shared `FrameClock` source for finite property writes. Repainting cancels work on discarded cells; the clock sleeps at rest. Coalesce queued daily/weekly updates so a second signal cannot discard a just-started completion effect.
- Replace WPF's temporary glow and popup shadow with sibling `Border.BoxShadow` layers. Reserve popup shadow padding while retaining the card's placement anchor. No animated `Effect`, transform-targeted `Animation.RunAsync`, or ambient loop is added.
- Register `quest-stamps` in `PanicSurfaces`; panic closes the readout, removes the flash, resets poses, and stops finite work. Shell close also stops it, including late pointer events.
- Add stepped-clock headless tests for daily and weekly completion, first-paint suppression, Motion Off, exact animation samples, shadow, same-turn stamp switching, repaint cancellation, close, and panic. Preserve the existing roster/art/copy/progress/reroll/language/navigation tests.
- Mark the single ledger row `wired`, never `verified`.
- Reference/import credits: the existing stamp port credits `parity/trunk` **7b6f94800e90a90fa443fb47efcfaeedc17eff0e**. This finish adapts the WPF stamp behavior also present at **d9dd21a7b66e548823c7305e34eb09e8ab6dc341** and the safe transform-write approach in **c18c2d8bb798ab9a8c75c676573252534955811b** (`TransformTween`). The base's own file wins; no existing file was overwritten with trunk, and no unused generic tween implementation was imported.

## Reference audit

Pinned reference: **a03cd7e20a48538efe3f3ab1be6af01970d50b6c**, `ConditioningControlPanel/MainWindow/MainWindow.QuestStamps.cs`, plus `ConditioningControlPanel/Services/MotionFx.cs` for `HoverLift` timing and easing.

The base ledger was `stub`. Its remaining user-visible gaps, confirmed against those files, were:

1. `AddStamp` did not remember completion transitions or call the completion pop; there was no finite scale overshoot or detachable gold flash.
2. `ZoomStamp` and `LiftStampHost` assigned final values immediately, missing WPF's durations/easings.
3. `ShowStampPopup` opened at full opacity without the timed entrance slide/fade or entrance-only behavior.
4. The popup lacked WPF's black shadow.

These gaps are now reachable through the live quest events and header pointer path. The existing implementation already covered the four-seat layout, daily/weekly/ghost distinctions, art/glyph/progress rendering, card text/reward/remaining counts, event marshaling, mod/language/reroll refresh, navigation, deferred leave, hover restoration, and move/deactivate dismissal. No remaining behavior depends on another unported feature; P122 is not used. P105's art-rule consolidation remains a coordination note for the overlapping art lane, rather than a missing user path in this row.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, `0 Error(s)`.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1`: exit 0, `0 Error(s)` after the final changes.
- `dotnet build <test-project> -c Release` (single-worker retries where needed): exit 0, `0 Error(s)` for all 11 Linux test projects listed in `.github/workflows/build.yml`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.Consent, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Scheduler, Avalonia.Report, Avalonia.GeneralSettings, and Avalonia.Snapshot. The final Avalonia suite rebuild also passed with `-m:1`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `512 Warning(s)`, `0 Error(s)`. WPF app and tests compiled; not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` (The first read-only-sandbox attempt could not create temporary files; the writable rerun passed.)
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: final exit 0, `Failed: 0, Passed: 2824, Skipped: 5, Total: 2829` (1 m 27 s). The first full pass, overlapping builds and disk exhaustion, reported 2 failures (`BoardTilesTests.A_frame_fits_the_budget`, 13.25 ms; `SessionRunnerTests.Ledger_IsPersistedBeforeTheSessionOverrides`, expected 1 / actual 0), 2822 passed, 5 model-dependent skips. No Core code or tests were changed; both failures cleared on the full repeat after freeing space and finishing builds.
- Headless `--render-view MainShellWindow`: exit 0, `1661x1002`, `1041373 bytes`, `[icons 14/6]`. Inspected the PNG: the four header stamps render beside the level chip. This static frame does not prove popup motion.
- Sandboxed `--nav-check`: exit 1, `nav-check: 2 failure(s)`: `OpenDeviceSettings reveals Devices (saw data)` and `an unknown section still opens the door and changes nothing`. These settings navigation paths are unchanged by this unit; they remain unresolved here.
- Some initial build attempts failed from exhausted space / an MSBuild child exit. Successful retries used one worker and removed only generated test output directories between builds.
- `git diff --check`: exit 0. Added-line cap is checked again before committing.

Avalonia test execution and fail-before-fix runtime proof are deferred under the task's explicit cloud-font limitation: `cloud-test-fonts` is absent from this pinned base. The new tests compile but have not been run; the supervisor must run `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~HeaderQuestStampsTests|FullyQualifiedName~PanicSurfacesTests"` with `DiffEngine_Disabled=true` on rcheck. No tests were weakened, removed, or skipped by this change. Windows runtime behavior cannot be exercised on Linux. Vosk model download is known blocked (403); the existing model-dependent Core tests report their own skips.

## Assumptions

- Keep WPF's existing English card copy and this head's existing Fluent icons.
- Sibling `BoxShadow` layers are the required Avalonia equivalent of WPF's temporary effect shadows; the platform rasterization may differ.
- Combine service notifications already queued in one UI turn into one snapshot repaint so completion motion remains visible for a signal shared by daily and weekly quests.
- Use the authorized fallback checkout because the supplied snapshot's Git metadata is read-only; compare commits, not the cloud branch name.
