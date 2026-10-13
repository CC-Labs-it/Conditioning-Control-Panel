# possession-core-c: move PossessionRemember ("it remembers") to CCP.Core

Unit C of the Services/Possession Core move. Base: `avalonia-port/game-pbp`.

## Already in CCP.Core/Services/Possession
- possession-core-a (`c55d73046`, merged into this base): `PossessionDeck`, `PossessionOffLimits` (name rule),
  `PossessionContracts` (enums + bark triggers), `EscapeKinds`, `Effects/ToastLines`, `Effects/RewritePools`.
- possession-core-b (`avalonia-port/cloud-possession-core-b`, **not** in this base yet): `PossessionAudio`.
  This unit does not touch PossessionAudio, so the two branches merge cleanly.

## Why not the director
The core-b draft named the director as unit C. `PossessionDirector.cs` (1156 lines) is bound to the WPF
Dispatcher, `DependencyObject`/visual-tree walks, `EmberAttribution` and the WPF effect contracts. Its pure
scheduling rules already live in Core `PossessionDeck`. Moving the rest needs the effect/host contracts to be
head-neutral first. That does not fit one finished unit. `PossessionRemember` is the next group with pure
rules, so it is this unit, and the director moves later (see below).

## What changed
- `git mv ConditioningControlPanel/Services/Possession/PossessionRemember.cs -> CCP.Core/Services/Possession/PossessionRemember.cs`
  (`git diff -M` shows it as `R056`). Core now owns the rules: only a Full Doki run arms the charge, and the
  intensity is read when the lockdown ends. The `LockdownPossessionRememberPending` flag is spent the moment
  startup reads it. The charge waits for the window and lands once, 20 s after the window is up. The clock
  resets while the window is hidden, the wait gives up after 3 min, and the charge never fires while a lockdown
  runs. Core also keeps the per-lockdown escape counter that `PromptAssembler` reads. It stays a static class,
  so `PossessionRemember.EscapeAttempts` in WPF `App` is unchanged.
- Seams: `App.Settings` became `CoreSettings.Current`, `App.Logger` became Serilog `Log`, and `App.Lockdown`
  became the `LockdownService` passed to `Install`. The `DispatcherTimer` became a `System.Threading.Timer`
  that posts `Tick` to the head's UI thread through `CoreDispatch.Post`, the same way Core `LockdownService`
  runs its countdown. The head supplies `Func<bool> isWindowReady` and `Action fire`.
- New WPF file `ConditioningControlPanel/Services/Possession/PossessionRememberCharge.cs` holds what stays
  WPF: whether the window is ready (`MainWindowRef` loaded and visible), and `Fire` (the tube check, the door
  lookup `BtnNavLockdown`/`DoorPlay`, the `EmberAttribution` ripple, and the bark line with its
  `GigglePriority` fallback). `App.OnStartup` calls
  `PossessionRemember.Install(Lockdown, PossessionRememberCharge.IsWindowReady, PossessionRememberCharge.Fire)`.
- One deliberate change: the 1 s tick used to run at `DispatcherPriority.Background`. It now arrives through
  `CoreDispatch.Post`, which is WPF's `BeginInvoke` at Normal priority. The 20 s delay tolerates that.
- The duplicate `RememberTrigger` constant was dropped. `PossessionBarkTriggers.Remember` (Core) is the
  single copy.
- New `Tests/CCP.Core.Tests/PossessionRememberTests.cs` (9 cases). There was no WPF test of PossessionRemember
  to move. The class shape (instance vs static aside) and test ideas were adapted from trunk `d959e0172`
  (CodeBambi, "PossessionRemember (a Full Doki lockdown is remembered once on the next launch)"). Trunk's
  head-neutral charge (edge pulse + bark) was not imported, because no head uses it yet.
- Docs: `POSSESSION.md` C2 paths, and the `docs/avalonia-parity.md` shell-possession row.
- No Avalonia caller yet. Avalonia has no Possession host, door ripple or bark road for it.

## Next units (Services/Possession, still in WPF)
1. **possession-core-d**: Warden bark rules + PossessionEvents (reactive adapter, trunk `a48c71cb8`), with their WPF
   tests (`PossessionBarkRulesTests`).
2. **possession-core-e**: head-neutral effect/host contracts (`IPossessionEffect`, `PossessionTarget`, `IPossessionHost`
   off FrameworkElement), then the effect / scene catalogs (`PossessionEffectCatalog*`, `PossessionSceneCatalog`).
3. **possession-core-f**: PossessionDirector scheduling into Core behind a host seam (trunk `d888c18a6`, `ffe18b811`,
   `021a76479`). After that, `PossessionAudio.Install` (core-b) can take the director directly.
4. Stays per head: the effects, Ghost, EmberAttribution, TransformLease, PossessionPointer, PossessionRememberCharge.

## Verification limits
- .NET SDK 10.0.112 (Ubuntu package). `dotnet build` succeeded for: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke,
  Tests/CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  succeeded. That compiles the WPF app with `PossessionRememberCharge` and the new `Install` call. It was
  **compiled, not run**: the WPF app and its tests need Windows. The real ember ripple and line on a Windows
  desktop were not exercised.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed." No embedded resources moved, so there is no
  baseline entry.
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1380 passed, 5 skipped, 2 failed. Both failures
  also fail on the untouched base (`origin/avalonia-port/game-pbp`, checked in a worktree), and both come from
  the container. `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` fails because the tests run
  as root. `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` fails because there is no IPv6
  loopback.
- `PossessionRememberTests`: 9/9 pass. Each of these mutations was applied to the Core file and turned the
  tests red, then reverted: (1) the Full Doki check removed; (2) the flag spend removed; (3) the running-lockdown
  check removed; (4) the hidden-window clock reset removed; (5) the 3 min give-up removed; (6) the escape
  counter fed `Repeat` instead of `Total`.
- `dotnet test Tests/CCP.Avalonia.Tests --filter LockdownTests|LockdownVeilTests`: 15/15 pass. No Avalonia code
  changed.
