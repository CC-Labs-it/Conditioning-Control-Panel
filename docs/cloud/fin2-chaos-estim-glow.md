# fin2-chaos-estim-glow: dependency NOOP

- Audited `chaos-estim-glow` against WPF at base commit
  `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.
- Kept the ledger row `stub`, removed repeated historical absence claims, and
  named the actual dependencies. No application code or tests changed.
- Fetched read-only `parity/trunk` at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; imported no code.

## WPF behavior and current coverage

WPF `Chaos/ChaosEStimOverlay.cs:320-500` defines a 92 DIP, transparent,
click-through, nonactivating halo window containing a 64 DIP violet radial
ellipse. Its scale goes from 0.88 to 1.10 over 160 ms with sine easing and
reverses; the pointer is sampled every 16 ms and the window is centered using
screen scaling. Arm realizes/reuses the window, shows it, raises it above video,
snaps it to the pointer, and starts following. Disarm stops following and hides
without destroying the window. Close stops the timer and destroys the singleton.
The Avalonia head already provides these visual/lifecycle behaviors through
`ChaosEStimGlow`, `ChaosEStimMath`, X11Pointer, and an armed-only FrameClock.
The existing panic surface `chaos-estim` closes both lightning and halo;
`ChaosRunHost` also closes them at run end and force shutdown.

Every remaining user-path gap:

1. Hidden precreation at run start (`ChaosEStimGlow.EnsureCreated`, WPF :329)
   is reached through `ChaosEStimOverlay.EnsureCreated` (:46) only when the
   E-Stim boon is active (`Services/Chaos/ChaosModeService.cs:453`). Avalonia
   lacks the E-Stim effect/charge lifecycle that would consume that window.
2. Live arming and disarming: WPF `ChaosModeService.cs:2761-2775` arms the
   bubble charges and halo together; `OnEStimArc` (:2789-2801) disarms when
   the final charge is spent. `Services/BubbleService.cs:356` implements
   `ArmEStim`. Avalonia's ordinary/live bubble field and toy buttons exist,
   but E-Stim charge storage, discharge, and the toy effect do not. In
   `ChaosRunHost.cs:517-551`, TickToys clears effect-active state and UseToy
   only calls `ChaosToyRules.Use` for gate/cooldown handling. Adding an aura
   caller alone would show a charge that cannot be spent.
3. Re-stacking a visible halo above mandatory video (`RaiseActive`, WPF :397)
   is called by `ChaosEStimOverlay.RaiseActive` (:100-103), in turn called
   by `ChaosModeService.RaiseGameLayerAboveVideo` (:745-761). Avalonia lacks
   that Chaos gameplay-layer restacking path. Arm already raises through
   X11Overlay; this does not supply the missing video-start/click integration.

Reproduce the caller audit at the pinned base:

```sh
git grep -n -E 'EnsureCreated|RaiseActive' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel/Chaos/ChaosEStimOverlay.cs
git grep -n -E 'ChaosEStimOverlay|case "e_stim"' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel/Services/Chaos/ChaosModeService.cs
git grep -n -E 'ArmEStim|EStimCharges|ChaosEStimOverlay\.(Arm|Disarm|Strike)' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Avalonia
```

The last command returns no matches (exit 1): there is no production charge
caller in the Avalonia head. WPF has live callers, so P122 (dead WPF code) cannot
justify marking this wired. Per the unit's pure-dependency rule, do not add
unused EnsureCreated/RaiseActive APIs or implement an unrelated toy/video lane.

## Verification limits

No new behavior was introduced, so no new tests or fail-before-fix
demonstration is applicable.
The existing tests cover halo centering, Arm/Disarm, compositor refusal, panic
teardown and Core pulse math. Real desktop pointer/DPI/stacking behavior and
Windows runtime behavior cannot be established by a Linux headless run.

## Assumptions

- The dependency NOOP rule takes precedence over completing unrelated E-Stim
  toy effects and Chaos mandatory-video integration in this overlay unit.
- Correcting the existing ledger row is documentation only; it stays `stub`.

### Commands and results

All dotnet commands used SDK 10.0.402 and Release configuration.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build Tests/<project>/<project>.csproj -c Release`: exit 0,
  0 errors for every Linux workflow test project: CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests,
  CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests,
  CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests,
  CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests,
  CCP.Avalonia.Scheduler.Tests, and CCP.Avalonia.Snapshot.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed
  (429 XAML references checked).
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests
  --no-build -c Release`: exit 1; 3951 passed, 2 failed, 5 skipped, total
  3958. Failures: `BoardTilesTests.A_frame_fits_the_budget` measured
  17.03 ms against the 12 ms ceiling, and
  `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`
  expected Loops but got Clips. Application code is identical to the base.
- Same command filtered to
  `FullyQualifiedName~Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls|FullyQualifiedName~A_frame_fits_the_budget`:
  exit 0; both passed in isolation. The full-suite failure remains reported.
- Same command filtered to `FullyQualifiedName~ChaosEStimMathTests`:
  exit 0; 3 passed, 0 failed.
- `ChaosEStimOverlayTests` and render-all were not run: the supplied cloud
  font-creation limitation applies; no cloud-test-fonts lane was found in
  this snapshot. The headless test project compiled with 0 errors. This
  is dependency analysis, not a P122 render proof or a visual verification.
- Windows code cannot be run on Linux. No real desktop windows were opened.
- `git diff --check`: exit 0.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  0 errors (554 warnings); compiled only, not run on Linux.
