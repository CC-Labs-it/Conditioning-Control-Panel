# Shell status FX completion

- Promote `shell-tab-fx-takeover-lab-status` from `stub` to `wired`, never `verified`.
- Remove superseded historical absence claims from the single ledger row. On the required base, all four callers already reach the registry through real feature state changes. No production behavior needs adding.
- Extend `StatusPulseLifecycleTests` with the WPF sine midpoint, full auto-reverse cycle, and repeated-refresh clock identity/phase assertions. These are regression assertions for existing behavior, not a claimed fail-before/pass-after fix.
- Add four numbered manual tester steps with WPF references and row coverage.
- No trunk imports were needed; no imported commit hashes to credit. No new surface, service, window, or panic registration is introduced.

## Pinned WPF comparison

Reference and implementation inspected at base `e77662b7d32441bd81c56c3ba3e32cb04b624936`.

| WPF behavior | Existing Avalonia user path / evidence |
| --- | --- |
| Blink pulse only in Running, not IdleReady | `BlinkTrainerTabView.RefreshStatusRow` calls the shell setter; `BlinkTrainerSessionTests.RunningSession_DotBreathes_AndTheCountdownFollowsVisibility` exercises session start/stop. |
| Haptics pulse while connected, including already connected on arrival | `HapticsTabView.RefreshHapticConnectionUi` calls the setter. Its attach repaint supplies WPF's arrival seed. `StatusPulseLifecycleTests.ConnectedHapticsGlowsWhenStudioOpens` uses the mock provider. |
| She's Listening pulse while armed, large blur | `MainShellWindow.SheListening.RefreshSheListeningStatus` calls `SetSheListeningStatusPulse(armed)`; registry requests large blur (26 rather than 10). `SheListeningTests` exercises arming. |
| Awareness master-on pulse | `AwarenessTabView.UpdateStatusIndicator` calls the setter using the master switch. This matches WPF `UpdateAwarenessStatusIndicator(on)`, not a requirement to wait for a separate observer. `AwarenessStatusPulseTests` toggles the real editor. |
| 0.22–0.95 sine breathing, 2.8 seconds each direction, stable repeated requests | Shared `BeatLoop.Breath` matches WPF SineEaseInOut plus AutoReverse; lifecycle regression assertions cover midpoint, peak, trough and clock identity. |
| Preserve feature-owned dot fill and opaque status indication | Effect-free sibling Border with BoxShadow, never dot opacity animation. Lifecycle and Awareness tests assert opacity 1 and null Effect. |
| Active window, not minimised, full ambient motion, performance glow and blur caps, effective visibility | `Pr4aAmbientAllowed` / `ApplyStatusPulse` and `EffectiveVisibility.Watch`; existing lifecycle and Awareness tests cover gates. |
| Mod retint and premium decoration refresh | `OnPr4aModChanged` repaints registry glows and calls `PremiumGateFx.RefreshAll`; the window/tab funnel also refreshes gates. Awareness test raises the mod event and checks colour and clock identity. Gate construction/decoration belongs to the already-wired `ctrl-premium-gate-fx` row. |
| Lazy setup and teardown | The first status setter installs the hooks once. Shell close stops clocks, disposes visibility watches, unsubscribes static events and clears requests. Lifecycle test checks close cleanup. |
| Takeover owns its orb; no second loop here | `BambiTakeoverTabView.axaml` hosts `TakeoverOrbFx`; `TakeoverOrb.ShouldRun` owns load/visibility/focus/minimise/motion gates. Existing `PerformanceSettingsMotionTests` and `PlaySpiralBleedTests` cover the control. |
| Lab retired; old decorations belong to Play | WPF partial lines 193–211 are a tombstone, not a live Lab handler. No Lab surface should be restored by this unit. |

Remaining WPF behaviors missing from this row: none found. This is a stale-status completion, not a dependency NOOP or a P122 promotion of an otherwise dead feature. The four status features have live callers in both heads.

Reproducible reference search (at the pinned base):

```sh
git grep -n -E 'Set(BlinkTrainer|Haptics|SheListening|Awareness)StatusPulse|OnLabTabShown' e77662b7d32441bd81c56c3ba3e32cb04b624936 -- ConditioningControlPanel/MainWindow
```

The live WPF callers are in `MainWindow.BlinkTrainer.cs:679`, `MainWindow.Haptics.cs:215`, `MainWindow.SheListening.cs:441`, and `MainWindow.Awareness.cs:375`. `OnLabTabShown` appears only in the tombstone comment.

## Verification limits

Commands use `-c Release` and .NET 10. `DiffEngine_Disabled=true` is set for tests.

- `dotnet build CCP.Core/CCP.Core.csproj`: exit 0, `0 Error(s)`.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: exit 0, `0 Error(s)`.
- `dotnet build` of every Linux test project named in `.github/workflows/build.yml`: exit 0, `0 Error(s)` for each. Projects: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `559 Warning(s)`, `0 Error(s)`. Windows application and tests compiled only; not run on Linux.
- Fresh final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`: exit 0, `86 Warning(s)`, `0 Error(s)`; no new assertion analyzer warnings.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `git diff --check`: exit 0. Structural ledger/test-plan check: exit 0, one row and four numbered WPF tester steps. `.github/scripts/test-plan-coverage.sh` is absent on this base.
- First `dotnet test Tests/CCP.Core.Tests`: exit 1, 4,046 passed, 1 failed, 5 skipped, 4,052 total. `BoardTilesTests.A_frame_fits_the_budget` measured 15.02 ms against its 12 ms limit while the Avalonia build was active. No production or test threshold was changed. Five existing Vosk tests skip because the model is unavailable.
- After all builds, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, `Passed! - Failed: 0, Passed: 4047, Skipped: 5, Total: 4052`. The original frame-budget test passes unchanged without compilation contention.

- No Windows runtime, real hardware, microphone, desktop notifications, or visual WPF comparison is exercised here.
- The supplied environment limitation applies: the cloud-font test workaround is absent from this base. Window-based Avalonia tests are compiled but not run; the focused regression assertions require the supervisor's GitHub runner.
- No application code changed, so no runtime fix or new behavior is claimed. Existing historical fail-proof statements are not re-certified by this audit.
- Assumption: this row owns the shared status registry and its existing gate-refresh calls. The premium padlock Effect implementation has its own `ctrl-premium-gate-fx` ledger row and remains unchanged.
