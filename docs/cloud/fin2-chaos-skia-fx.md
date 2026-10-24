- Wire ordinary Chaos bubble pops to the existing Skia FX surface before the gameplay callback. Use the bubble's screen scaling to convert its global DIP centre to physical pixels, payload tint at scale 1, or WPF snap cyan at scale 1.3 for a direct live pop. Preserve pause, duplicate-pop and persisted switch guards.
- Prewarm the idle surface from BeginRun, matching WPF; do not subscribe frames or show it until activity. Reuse the existing `chaos-skia-fx` panic surface and run-end cleanup; no new surface or engine dependency.
- Add focused headless coverage for actual emission, centre/body rendering, payload/snap colours, duplicate suppression, pause, disabled FX and idle prewarm. The two burst cases failed before the caller fix with `Assert.NotNull` (no active overlay), then passed after it.
- Replace the single ledger row's repeated historical text with the current reachable path and honest remaining gaps. Keep `stub`.
- Source: WPF at base `a65dbbe2f4935a4797b32efd87513054dd39e1d2`. Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported or existing implementation overwritten.

## WPF comparison and remaining gaps

Pinned inspection commands (run against the base SHA above):

```sh
git grep -n 'ChaosSkiaFxOverlay\.' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel CCP.Avalonia
git show a65dbbe2f4935a4797b32efd87513054dd39e1d2:ConditioningControlPanel/Chaos/ChaosSkiaFxOverlay.cs
git show a65dbbe2f4935a4797b32efd87513054dd39e1d2:ConditioningControlPanel/Services/BubbleService.cs
git show a65dbbe2f4935a4797b32efd87513054dd39e1d2:ConditioningControlPanel/Services/Chaos/ChaosModeService.cs
```

The existing Core `ChaosFxLayer` already supplies the particle counts, palette, bloom, trail direction, lightning jitter, ripple front and cursor breathing. The head already supplies UI-thread dispatch, compositor refusal, active-only frames, three idle frames, eight-second hide grace, panic and run teardown. These were not missing render implementations.

| WPF behavior still absent on entry | Disposition |
| --- | --- |
| Bubble.Pop payload bursts (`BubbleService.cs:4687-4688`) | Wired through BubbleOverlay.PopChaos; real FALL IN / treat click path exists on this base. |
| Idle prewarm (`ChaosModeService.cs:412`) | Added in BeginRun. |
| Rabbit/GG trails every 22 px, travel hint, shimmer (`BubbleService.cs:3934-3940,3987`) | Missing: darter/GG rabbit movement, catch and shimmer gameplay. |
| E-Stim chain strikes, Body Buzz wave strike/ripple, Bound hops (`BubbleService.cs:441,479,495,1752`) | Missing: those gameplay callers; the standalone E-Stim drawing surface is insufficient. |
| Ripple cast / Size Queen shockwaves (`BubbleService.cs:1662,1675`) | Missing: ripple cast and pickup gameplay. |
| Rabbit-Caller arm/disarm/cursor polling (`ChaosModeService.cs:2856,2873,2886`) | Missing: Rabbit-Caller hold/input lifecycle. |
| Tease risk-red 1.4 burst (`BubbleService.cs:4798`) | Missing: Tease gameplay. |
| Unified compositor routing and per-monitor DPI | Missing: Avalonia unified Chaos compositor hosts; stage-wide legacy path currently uses primary scaling. WPF legacy also uses one window DPI; the compositor is the per-monitor path. |
| RaiseActive above mandatory video (`ChaosModeService.cs:753`) | Missing: Chaos mandatory-video lifecycle/re-stack caller. Existing effects already raise on activity. |
| Real mixed-DPI/live run proof | Requires desktop/compositor validation, unavailable here. |

WPF CompleteDefuse (`BubbleService.cs:4589`) and fuse detonation do not emit the Burst call; this change does not add it to those paths. Direct live Pop uses snap colour and scale, while the normal held defuse remains separate. Other pickup/mimic effects can use the generic Pop when their gameplay lands. The repeated ledger claims that FALL IN and the entire bubble field were absent were stale. WPF has live callers, so P122 (dead WPF code) does not apply. Remaining gameplay and compositor work is outside this one FX unit, and the row must not be called wired.

## Assumptions

- Match the existing stage-wide legacy renderer rather than introducing another compositor or porting several gameplay units into this branch.
- Keep all existing simulation, rendering, platform refusal and panic behavior; use scale 1 only for the existing model-only/headless fallback with no bubble windows.

## Verification limits

Verification results are recorded below after the required checks complete. Windows code can be cross-compiled here but cannot be run. No real desktop, mixed-DPI monitor, compositor or Windows z-order behavior is claimed. All Avalonia tests use the assembly's isolated user-data sandbox.

- SDK: .NET 10.0.402. Initial snapshot HEAD differed from the required base; fetch failed on read-only Git metadata. Used the authorized fallback clone and verified clean HEAD exactly matched the required base before branching.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <test-project> -c Release`: all 12 Linux-compatible test projects referenced by `.github/workflows/build.yml` built with exit 0 and 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 2 warnings, 0 errors. WPF application and tests compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0; `All Core guards passed.`
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,953 passed, 5 skipped, 0 failed (3,958 total). The five speech tests require the unavailable Vosk model; no tests were weakened, deleted or newly skipped.
- With the same environment, `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~ChaosSkiaFxOverlayTests`: exit 1; 3 passed, 1 failed, 0 skipped. Both new burst/prewarm cases pass, as does the existing direct overlay lifecycle/render/panic case. Existing `RunEndAndRecapDismissCloseTheField` fails with NullReferenceException because StartRun leaves `ChaosRunHost.Overlay` null before FinishCountdown. This also failed before prewarm was added. Thus run-window integration cannot be claimed green here; no real desktop or Windows behavior was tested. The direct tests do render the real Core field and check its pixels.
- Before the caller fix, `--filter FullyQualifiedName~ChaosPopEmitsOnce`: exit 1; both new cases failed because no active overlay existed. After the fix they pass, including payload/snap body colour, location, one-emission guard and idle prewarm checks.
- Infrastructure retries: overlapping builds first hit an Avalonia output-file lock; a sequential pass then exhausted disk copying native dependencies. Removed only disposable build outputs between projects, retried Consent and WPF with `-m:1`, and rebuilt the final Avalonia test assembly (exit 0). No source or test assertions were removed to work around infrastructure.
- Other test assemblies were compiled, not executed. No full `--render-all` or live mixed-DPI run was performed; P122 is not being used.
