# fin2-chaos-effect-banner

- Dependency-only unit: no runtime code changed. Keep `chaos-effect-banner` **stub**;
  showing a bonus banner without its gameplay effect would misrepresent the run.
- Replace the repeated historical ledger text with the current missing callers.
  The hub run-start claim was stale: FALL IN already posts `ChaosRunHost.StartRun`
  (`ChaosHubWindow.axaml.cs:1783`).
- Base and WPF reference pinned to `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`;
  no code imported, so no trunk implementation credit is claimed.

## WPF behaviour and remaining dependencies

The cited WPF `Chaos/ChaosEffectBannerOverlay.cs` is already represented by the
Avalonia window and Core strip: pre-create at run start; one live label per id;
arrival order; duplicate Show leaves the existing text/art unchanged; End of an
unknown or already fading id does nothing; End removes the live id immediately,
so re-Show can append beside the fading label. Art uses `artKey ?? id` with text
fallback (uppercase 34px, accent fill, dark 2.6px outline). The strip is centred,
80 DIP tall, at primary work-area top + 6; labels have 18 DIP horizontal margins,
56 DIP art height and centre-origin scaling. Fade-in is 200 ms, the sine throb is
1.0 to 1.03 over 850 ms each way, and fade-out is 380 ms from current opacity.
It stays alive while empty; teardown clears and closes it. Show/End marshal to
the UI thread, Show raises the window, and RaiseActive supports video restacking.
The existing platform seam refuses non-composited/non-click-through windows once
per run rather than intercepting desktop input. No new surface needs registration:
existing panic dispatch reaches `ChaosRunHost.ForceShutdown` and banner teardown.

Every missing live banner behaviour below belongs to unported gameplay or the
unported video game-layer coordinator. Locations are in WPF
`Services/Chaos/ChaosModeService.cs` at the pinned base:

| Missing user-visible behaviour | WPF caller / termination | Dependency |
| --- | --- | --- |
| `slowmo`: Time Slow or Pendulum; cyan fallback, pendulum art override | ActivateSlowMo:2414-2415; EndSlowMo:2463 | Darter catch and timed Pendulum slow-mo runtime (6s plus bonus; chained catches extend by 0.8s; Pendulum lasts 2.5s; duplicate Show preserves live label) |
| `vibe`: VibePopping; orange fallback | UseToy:2707; TickActiveToys:2944 | Vibe sweep and buzz window, including hover-pop Afterglow extension before End |
| `dvd`: Porn DVD; pink fallback | UseToy:2731; TickActiveToys:3027 | DVD toy launch and lifetime; End only when no toy DVD is active, not for unrelated text DVDs |
| `estim`: E-Stim; violet fallback | UseToy:2771; OnEStimArc:2796 | Charged pops and discharge callback; End only when final charge is spent |
| `rabbits`: Rabbit Storm; magenta fallback | RabbitAimTick:2908; TickActiveToys:2992 | Aimed Rabbit Caller capstone, 10s storm (not ordinary summon); End when storm expires |
| `freeze`: Freeze; icy blue fallback | ActivateFreeze:3044; EndFreeze:3060 | Freeze pickup/toy, 3.5s hold and release; repeat activation refreshes hold without duplicating label |
| Re-raise live strip above mandatory video in gameplay-band order | RaiseGameLayerAboveVideo:758, guarded by spawning and PinTopmost | Pinned game-layer/video restacking coordinator |

The windows/visual models for several effects exist; their gameplay activation
and completion paths do not. In Avalonia `ChaosRunHost.UseToy:548` only invokes
`ChaosToyRules.Use` (gate/cooldown); `TickToys:519` drops all active flags. SpawnTick
excludes freeze variants (`:162`). There is no slow-mo or RabbitAimTick equivalent
and no RaiseGameLayerAboveVideo coordinator. Wiring labels to cooldowns would give
incorrect lifetimes, especially for Afterglow, charges and DVD activity.

Reproduce the caller evidence at the pinned SHA:

```sh
git grep -n 'ChaosEffectBannerOverlay' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel/Services/Chaos/ChaosModeService.cs
git grep -n 'ChaosEffectBannerOverlay' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Avalonia/Views/Chaos/ChaosRunHost.cs
git grep -n 'ChaosRunHost.StartRun' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- CCP.Avalonia/Views/Chaos/ChaosHubWindow.axaml.cs
```

The Avalonia host matches only EnsureCreated:104 and CloseActive:463/:583;
no production Show, End or RaiseActive callers exist. WPF has live callers, so
P122 (dead WPF caller exemption) does **not** apply. No render-all claim is made.

## Verification limits

No new behaviour or test was added for this documentation-only dependency unit.
.NET SDK 10.0.402; builds use Release configuration.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- All 12 Linux-compatible test projects listed in `.github/workflows/build.yml`
  compiled: CatalogueStartup, Consent, GeneralSettings, Language, Migration,
  Report, Scheduler, Snapshot and main Avalonia tests; Core Content, Settings
  and main Core tests. Each `dotnet build` exited 0 with 0 errors.
- Test-project builds also passed
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
  Outputs were removed between projects to fit the temporary filesystem. An
  earlier batch exhausted disk space (exit 134); sequential clean-output builds
  resolved that infrastructure failure without changing source or tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (3 existing
  baseline entries; 429 XAML references checked). The first restricted invocation
  could not create scratch files; the successful rerun had temporary-file access.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`:
  exit 0; **3953 passed, 0 failed, 5 skipped** (existing Vosk model-dependent tests;
  the model is unavailable in this environment). The five banner-strip tests are
  included in this full suite. No test was weakened or changed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build
  --filter FullyQualifiedName~ChaosEffectBannerOverlayTests`: exit 1;
  **1 passed, 1 failed**. The run-start test fails its line 40 `Assert.NotNull`
  because the banner instance is null after StartRun. This is an unchanged-base
  test in a documentation-only diff. The task reports a cloud font-creation
  limitation; the assertion output alone does not establish that root cause.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors.
  WPF app and tests compiled for Windows but could not be run on Linux.
- No Windows runtime tests, live desktop checks, or render-all were run. No P122
  exemption or visual-verification claim is made.

## Assumptions

- Follow the task's dependency-only exception instead of importing the entire
  missing toy/power-up runtime into this banner unit.
- A real bonus activation and its matching completion must own each banner;
  cooldown success alone is not an effect activation.
