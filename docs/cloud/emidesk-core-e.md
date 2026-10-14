# emidesk-core-e: EmiChains data + rules and EmiNudges into CCP.Core

Base: `avalonia-port/companion-core-b`. Branch: `avalonia-port/cloud-emidesk-core-e`.

Already in Core for this area before this unit: `EmiState` (incl. `PetGistCount`/`RingGistCount`
and `NoteNudgeFired`), `EmiLineEngine`, `EmiKnock`, `EmiChrome`, `EmiProps`, `EmiMuteRule` and the
rest of `CCP.Core/Services/EmiDesk`. Neither `EmiChains` nor the nudge machine had a Core copy, so
nothing is duplicated; the Avalonia head's private `RestFace = "0_0"` copies now read the Core one.

## What changed

- **`git mv` `ConditioningControlPanel/Services/EmiDesk/EmiChains.cs` -> `CCP.Core/Services/EmiDesk/EmiChains.cs`.**
  Core keeps the chain data and rules: `EmiFrame`, `EmiChain`, `EmiChainHooks`, the face sets, the
  pose sheet, `FaceBodyFrame`/`FrameForFace`/`FrameKey`, the sway dials, the outfit gate
  (`OutfitName`/`OutfitDir`/`OverFileName`), `BodyPath`/`OverPath` (AppContext.BaseDirectory +
  Serilog, as `EmiProps` already does), `Chains`/`Get`, `MakeSay`, `SayHoldMs`. No retune.
- **The player stays in the head.** The `DispatcherTimer` player is now
  `ConditioningControlPanel/Services/EmiDesk/EmiChainPlayer.cs` (was the nested
  `EmiChains.Player`; body unchanged apart from qualifying `EmiChains.FrameKey`).
  `EmiDeskWindow` is the one caller.
- **`git mv` `ConditioningControlPanel/Services/EmiDesk/EmiNudges.cs` -> `CCP.Core/Services/EmiDesk/EmiNudges.cs`.**
  `EmiNudgeMachine`, `IEmiNudgeWorld` and `EmiNudgeWorld` move whole. The world's App-only half
  (is the desktop quiet?) is now the seam `EmiNudgeWorld.QuietProbe` (`Func<bool>`); unset or
  throwing reads as "not quiet". Shape adapted from trunk `bf7785067` (parity emi: the nudge
  machine in Core).
- **WPF wiring:** `EmiDeskService.NudgeQuiet()` carries the exact old `Quiet` body (out, AskSituationOk,
  window visible, no live chain, no hold); `App.OnStartup` sets
  `EmiNudgeWorld.QuietProbe = () => EmiDesk?.NudgeQuiet() == true` next to the EmiLineEngine probes.
  WPF behaviour is unchanged.
- **Avalonia (code-behind only, no UI):** `EmiDock` and `EmiDeskWindow` read `EmiChains.RestFace`
  instead of their own `"0_0"` copies.
- **Tests:** `git mv` `Tests/ConditioningControlPanel.Tests/EmiNudgeMachineTests.cs` ->
  `Tests/CCP.Core.Tests/` (identical to trunk `bf7785067`'s Core copy). New
  `EmiNudgeWorldQuietProbeTests` (probe yes/no, unset/throwing = not quiet, machine silent when
  not quiet) and `EmiChainsCoreTests` (chain/pose table integrity, junk-safe lookups, outfit gate,
  `MakeSay` cadence, `SayHoldMs` formula). The art-on-disk outfit tests stay in the WPF suite,
  which ships the art.

## Next units

- Wire the Avalonia chain player (a Core-clocked twin of `EmiChainPlayer`) and drop the
  `PlayChain`/`Say`/`SetPose`/`PaintOutfitOver`/`RestartIdleBeats` ponytails in
  `CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml.cs`; their comments still say
  "EmiChains is WPF-only" and should be rewritten then. Trunk `4bf755c5d` is the reference.
- Avalonia nudge poll: set `EmiNudgeWorld.QuietProbe` on the Avalonia head and port the 5 s poll
  (trunk `bf7785067`, `EmiDeskService.Nudges.cs`).
- `EmiAlive` into Core (needs one Core geometry type for `GazeTarget`/`WithinApproach`).
- Remaining WPF-only EmiDesk rules: `EmiNames`, `EmiTargets`, `EmiTopmostRule`, `EmiBubblePlacement`.

## Verification limits

- **Nothing was compiled or run in this session.** The .NET 10 SDK is not on the image, and the
  environment's network policy denies `dot.net` / `builds.dotnet.microsoft.com` (403 at the proxy),
  so `dotnet-install.sh` could not run. Not run: `dotnet build` of CCP.Core, CCP.Avalonia and the
  test projects; `dotnet test Tests/CCP.Core.Tests`; the Avalonia test filters. CI on this branch
  is the first compile.
- Run: `bash .github/scripts/core-guards.sh` -> "All Core guards passed."
- Reviewed by hand for: no `System.Windows`/`App` in the moved Core files, no remaining
  `EmiChains.Player` references, no duplicate type names across Core/heads, `NudgeQuiet` uses the
  same members the old `Quiet` used (`IsOut`, internal `AskSituationOk`, `Window`, `ChainLive`,
  `EmiLineEngine.Instance.HoldActive`).
- WPF (`ConditioningControlPanel`, its tests) cannot build on Linux; `EmiChainPlayer` and
  `NudgeQuiet` are Windows code that has not been compiled here.
