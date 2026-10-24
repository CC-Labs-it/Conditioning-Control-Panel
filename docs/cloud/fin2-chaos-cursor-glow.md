# fin2-chaos-cursor-glow

- Documentation-only dependency outcome; no application or test code changed.
- Reconciled the single `chaos-cursor-glow` ledger row against base
  `a65dbbe2f4935a4797b32efd87513054dd39e1d2`; it remains **stub**.
- Removed repeated historical absence claims: the standalone halo and Skia cursor
  API already exist, and FALL IN now starts a run. No new runtime behaviour is claimed.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No trunk code imported, so there are no imported commit credits.

## WPF comparison and remaining dependencies

All WPF references below are pinned to base commit
`a65dbbe2f4935a4797b32efd87513054dd39e1d2`.

`ConditioningControlPanel/Chaos/ChaosCursorGlowOverlay.cs` supplies the 76 DIP
transparent pink/gold halo, centred transform, 0.85–1.12 SineEase pulse with a
620 ms forward leg and reverse, no-activate click-through window and physical
pixel-to-DIP positioning. Those visuals and positioning already exist in the
Avalonia overlay and `ChaosCursorGlowMath`. Its armed-only 16 ms Render-priority
poll stops on Disarm; panic and all three run teardown paths close the window.
The existing tests cover those seams and refusal caching. No new surface needs
panic registration.

Remaining live WPF behaviours and their prerequisites:

1. **Rabbit Caller skill lifecycle** (`ChaosModeService.cs:2751–2754`,
   `2844–2914`): skill power sets pending rabbit count and capstone state, arms
   the selected renderer, and polls every 16 ms at Render priority. The initial
   press is swallowed; pointer sampling continues during pauses/drafts but a
   summon requires a fresh left-button press while the field is live. It disarms
   before spawning rabbits at the click with ±60 px scatter; maxed power starts
   a ten-second storm, followed by sound and event copy. Invalid run state and
   teardown cancel the pending call. Avalonia `ChaosRunHost.UseToy` only calls
   `ChaosToyRules.Use` (gate/cooldown/charges); there is no effect dispatch or
   `ArmRabbitCall`/`RabbitAimTick`. Its ordinary bubble field is now present,
   but the Rabbit Caller driver remains unported. Merely arming a decorative
   halo from UseToy would leave it waiting forever without the summon contract.
2. **Conditional pre-create and renderer selection** (WPF service `451–452`,
   `2856`, `2873`, `2886`): legacy EnsureCreated only when Rabbit Caller is
   equipped and Skia FX disabled; Arm/Disarm/Move route to the selected renderer.
   Avalonia Skia `ArmCursorGlow`/`DisarmCursorGlow`/`MoveCursorGlowToPx` and Core
   cursor painting already exist. They lack the same skill driver. Legacy
   pre-create is absent; it belongs with that run-start skill lifecycle. The
   legacy Avalonia breath also restarts its epoch on each Arm; WPF keeps its
   animation epoch across hide/rearm. Preserve that phase when implementing
   the pre-created skill lifecycle (without a hidden 60 Hz Avalonia loop). WPF
   documents it as protection against shared render-thread layered-window churn.
3. **Video stacking coordinator** (WPF overlay Arm/RaiseActive; service
   `RaiseGameLayerAboveVideo`, `744–770`): re-stack on arm, mandatory-video start
   and video click, respecting the pin-topmost setting and gameplay-layer order.
   Avalonia has individual `X11Overlay.Raise` calls but no equivalent game-layer
   coordinator. The dormant legacy halo cannot exercise this contract until
   Rabbit Caller is live; adding a method without its callers would not wire it.
4. **Platform pointer service**: WPF samples GetCursorPos and the left button
   globally. The existing Avalonia halo uses `X11Pointer.Read`, which explicitly
   returns null outside Linux/X11 and cannot see native Wayland surfaces. A
   desktop-wide pointer/button backend for Windows, native Wayland and macOS is
   a shared input dependency, not a visual halo change. No platform support is
   claimed by this documentation-only unit.

## Reproducible caller evidence

Run at the pinned base:

```sh
git grep -n -E 'ArmRabbitCall|RabbitAimTick|ChaosCursorGlowOverlay[.]Arm' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel/Services/Chaos/ChaosModeService.cs
git grep -n -E 'ArmRabbitCall|RabbitAimTick|ChaosCursorGlowOverlay[.]Arm' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Avalonia
git grep -n -E 'ArmCursorGlow|DisarmCursorGlow|MoveCursorGlowToPx' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Avalonia/Views/Chaos/ChaosSkiaFxOverlay.cs
```

WPF has live callers, while the Avalonia driver search has no matches. P122
(dead WPF path) does not apply. The row cannot become wired. The next unit needs
the Rabbit Caller toy-effect/aim/summon lifecycle and its shared platform/video
services; this unit follows the requested pure-dependency NOOP rule.

## Assumptions

- Keep the diff to dependency documentation rather than expanding this visual
  unit into the Rabbit Caller gameplay and shared desktop-input/video services.
- Keep `stub` until the real skill path reaches both renderer choices and all
  required lifecycle behaviour. Existing tests are evidence of seams, not wiring.

## Verification limits

- Application code is unchanged; no new behavioural tests or fail-before-fix
  demonstration applies to this dependency-only documentation change.
- Avalonia test classes `ChaosCursorGlowOverlayTests`, `ChaosSkiaFxOverlayTests`
  and `ChaosRunHostTests` are compiled, but are not executed here, following the
  supplied cloud font-creation limitation. The shared dispatcher and cursor-glow
  fixture do not contain the cloud font setup fix. The supervisor must run the
  relevant classes on GitHub (rcheck).
- No live desktop, native Wayland, macOS or Windows session is available. Native
  click-through, cursor tracking, video z-order and Windows code are not run.
- No Vosk model download or speech-runtime claim; the supplied environment notes
  report that model download is blocked (403).
- Initial test-project builds exhausted temporary disk space while copying
  native dependencies; generated outputs were removed and builds repeated one
  project at a time. A stale MSBuild child-node failure was retried separately.


### Completed checks

All builds used .NET SDK 10.0.402 and Release configuration.

| Command / project | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 48 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 192 warnings, 0 errors |
| `dotnet build Tests/<project>/<project>.csproj -c Release` for all 12 Linux test projects below | 0 | 0 errors each |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true` | 0 | 3953 passed, 5 skipped (Vosk), 0 failed; 3958 total |
| `bash .github/scripts/core-guards.sh` with writable temporary storage | 0 | All Core guards passed |
| `git diff --check` | 0 | No whitespace errors |

Linux test projects compiled: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`,
`CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`,
`CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
`CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`,
`CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`,
`CCP.Avalonia.Snapshot.Tests`.
The Report project retry used `--no-restore -m:1 -nr:false` and finished with
0 warnings and 0 errors. The five speech skips are existing model-dependent
conditions, unchanged by this unit; no test was weakened or removed.

The conditional Windows-targeted build also succeeded (exit 0):

```sh
dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false
```

Result tail: 554 warnings, 0 errors. NuGet 503 retries recovered during restore.
This compiles the frozen WPF reference and its tests; neither was executed here.
No `--render-all` claim is made: this is a dependency NOOP, not P122 or visual
verification. All required Linux builds completed with zero errors.
