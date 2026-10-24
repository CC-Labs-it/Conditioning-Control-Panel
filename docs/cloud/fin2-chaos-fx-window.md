- Wire existing bubble callbacks to the FX window: treat/prism, defuse, Snap Chain, shield, collar, Unleashed and bare detonation. Match WPF palette, strength, config gate and callback order; combo micro/×10 feedback intentionally replaces the preceding pulse on the same layer, just as WPF does.
- Add combo threshold crossing tracking (25/50/100), reset on bare hit and new run, and focused headless tests for outcome colors/peaks/fade, combo replacement/crossings/reset, disabled flashes and manual pause. No new window or panic surface: the existing ForceShutdown/CloseFx lifecycle still owns this window.
- Restore WPF gentle pulse rise (300ms plus the unchanged 260ms fade), with a focused Core timing test.
- Replace the duplicated historical ledger text with one honest `stub` row. FALL IN already reaches StartRun on this base; the old no-caller claim was stale.
- No trunk code imported. Read-only `git fetch origin parity/trunk` resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing head files won. Behavior adapted directly from the frozen WPF reference at base `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.

## Reference and remaining gaps

At the pinned base, `ConditioningControlPanel/Chaos/ChaosFxWindow.cs` defines heat/edge/vignette order, brush geometry/stops, opacity limits, 40ms (gentle 300ms) rise/260ms pulse fade, 50/650ms freeze burst, 180ms edge hold in, 450ms edge out, 400ms heat in and 600ms heat out, plus focus-free topmost raising. The Avalonia window already implements these layers and finite ramps, primary screen placement, click-through refusal and cleanup; this unit adds the missing gentle timing and live bubble-event pulse path.

`ConditioningControlPanel/Services/Chaos/ChaosModeService.cs` is the caller reference: BeginRun :500; Pulse gate :1672; prism/treat :1821-1867; defuse :1984-2052; detonation :2055-2130; freeze :3038-3064; combo :3067-3101; heat :3131; close :3186/:3239. `rg -n 'Pulse\(|_fx\?|_fx\.'` over that file shows live WPF callers; P122 dead-code treatment does not apply.

Remaining WPF behaviors (row stays stub):

- Freeze pickup and freeze active toy start the frost burst/held icy edge; release fades the edge even if flashes have since been disabled. The head has no freeze pickup/toy execution/timed release yet.
- Special bubble feedback: heart/shield gain, gold droplet/golden, darter catches/misses, Brittle, Tease trigger/deny, treat escape/rot, insufficient-focus warning, Body Buzz shockwave and other unported bubble branches.
- Active effects/toys and boons: slow-mo cue, rabbit/e-stim cues, shield recharge/gain, life-cost/boon feedback and draft-enabled wave-clear pulse. Existing UseToy only applies the gate/cooldown; the head runs draft-off wave transitions. These pulses belong with their feature's real execution path.
- `RaiseToTopmost`: both mandatory-video game-layer re-raising (:743-776) and periodic KeepChromeTopmost (:787 onward) are absent. The periodic raise is an independent remaining stacking gap; mandatory video also awaits the real payload firer (currently a logging no-op). Re-raising this window alone without the ordered bubble/chrome/attention layers would not reproduce WPF.

Assumptions: preserve WPF's same-callback replacement behavior rather than combining pulses; reuse the current head and Core field callbacks instead of importing another run host. No desktop or profile is used by new tests (existing headless sandbox harness).

## Verification limits

Verification results are recorded below before commit. Headless tests are compiled but not executed under the supplied cloud font limitation; the supervisor must run `ChaosFxWindowTests` and `ChaosRunHostTests` on rcheck. Windows native stacking and transparency cannot be exercised here. No render-all or desktop smoke was run; this row is not marked wired or verified.

### Commands and results

- Exact checkout verification: initial HEAD differed; fetch/reset succeeded, and work began at `a65dbbe2f4935a4797b32efd87513054dd39e1d2` on the requested new branch. The remote base ref was populated explicitly and still equals this commit.
- .NET channel installation failed discovery (HTTP 403); pinned SDK `10.0.100` installed successfully.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1`: exit 0, 0 errors (existing warnings).
- `dotnet build` in Release for every Linux test project in `.github/workflows/build.yml`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Migration, Avalonia.Scheduler, Avalonia.Report, Avalonia.Consent, Avalonia.GeneralSettings and Avalonia.Snapshot. All compile with 0 errors. Initial overlapping builds collided on Avalonia's generated output; the sequential build succeeded. Language initially encountered MSB4166; its sequential `-m:1` retry succeeded. Tests were not changed to address either infrastructure issue.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors, 554 warnings. Windows/WPF tests compiled only, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 3953 passed, 5 skipped, 1 failed (3959 total). The sole failure is unrelated `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, expected Loops, actual Clips. A full `--no-build` repeat produced the same totals. The five skips include speech tests requiring the unavailable Vosk model.
- Focused Core invocation including the unrelated failure and FX envelope class: exit 0, 6 passed. The unrelated media failure therefore reproduces in the full suite but not in isolation; it was not changed or weakened.
- Negative control: temporarily restore the fixed 40/300ms keyframes while retaining the new rise argument, run `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~Gentle_pulse_rises`: exit 1 as required, expected 0.235 at 150ms, actual 0.271154. Restore the fix, then `--filter FullyQualifiedName~ChaosFxEnvelopeTests`: exit 0, 5 passed.
- Headless `ChaosFxWindowTests`/`ChaosRunHostTests` execution is deferred under the supplied cloud font limitation. Their project compiles with zero errors; new head assertions are not claimed fail-proven here. Run on rcheck with `DiffEngine_Disabled=true` and `--filter 'FullyQualifiedName~ChaosFxWindowTests|FullyQualifiedName~ChaosRunHostTests'`.
- No PR opened, no other branch pushed; no native Windows or real-desktop behavior exercised.
