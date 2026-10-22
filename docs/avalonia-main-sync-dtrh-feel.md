# Main-sync delta ledger: DTRH feel rework (dtrh-feel 17-pace)

Range: `git log --no-merges 11d7c3afc..46e9e908d` (27 commits), imported by merge `23054e370`
("merge: import head dev DTRH feel rework ... into Avalonia continuation").

Web pages: every file the range added or changed under `ConditioningControlPanel/Resources/web/dtrh/`
(54 files) is byte-identical at `Assets/web/dtrh/` on `23054e370` (blob compare, lane dtrh-feel-port).
No new non-text asset types (no audio/images), so `CCP.Avalonia.csproj` links need no change.

Status legend: **ported-by-merge** = web-only change now served from `Assets/web/dtrh`;
**ported-here** = landed in lane `avalonia-port/dtrh-feel-port` or `avalonia-port/game-dtrh-host`; **needs-port** = open, with reason;
**n/a** = nothing to port, with reason.

Lane **game-dtrh-host** (slice A, parity row `game-dtrh`) registered `dtrh/index.html` on
`CCP.Avalonia/Views/Games/GameWindow.cs` (`GameWindow.Dtrh.cs`): init with `motionLevel`, the local manifest, the run
lifecycle, Escape as the game's pause (`MainShellWindow.PanicKey.cs` -> `PanicPolicy.GameClaimsEscapeAsPause` /
`KeptEscapeGoesTo` -> kept-escape) and panic closing it (PanicSurfaces "games"). The remaining host rows need the
`DtrhAssetManifest` remote half (online media copies, loops lane, loops-dry back-off), which lands with lane
**game-dtrh-host-b**.

Counts: 22 ported-by-merge, 2 ported-here, 3 needs-port, 0 n/a (total 27). A row whose host part is open counts as needs-port.

| # | sha | subject | WPF files outside web/dtrh | Avalonia/Core counterpart | status |
|---|---|---|---|---|---|
| 1 | `d0d263396` | feat(dtrh): shared motion level module for the feel pass | none | `Assets/web/dtrh/shared/motion.js` | ported-by-merge |
| 2 | `92f83a77e` | feat(dtrh): feel pass, audio lane - pop ladder, failure subtracts, depth drives the mix | none | `Assets/web/dtrh` | ported-by-merge |
| 3 | `89f028807` | feat(dtrh): feel pass, field lane - squash, breath, reward flights | none | `Assets/web/dtrh` | ported-by-merge |
| 4 | `753eb26c4` | feat(dtrh): feel pass, chrome lane - motion levels, press states | `Services/Chaos/DtrhHostService.cs` (init `motionLevel`), `Services/Chaos/DtrhMotionWire.cs` (new), `Tests/.../DtrhMotionWireTests.cs` (new) | `CCP.Core/Services/Chaos/DtrhMotionWire.cs` (git mv; `App.Settings` -> `CoreSettings`, WPF caller unchanged); tests moved to `Tests/CCP.Core.Tests/DtrhMotionWireTests.cs` (+ `CurrentReadsThePlayersSetting`), fail-proven (~/ccp-port/evidence/review-dtrh-feel-port/fail-proofs.txt); web part in `Assets/web/dtrh` | ported-here (wire word, Core; init `motionLevel = DtrhMotionWire.Current()` in `CCP.Avalonia/Views/Games/GameWindow.Dtrh.cs` DtrhInitMessage, lane game-dtrh-host, test `GameWindowTests.Dtrh_LabDoor_BootRunLifecycle_AndCloseBanks`) |
| 5 | `952d91f4d` | fix(dtrh): feel pass seams - pulse cap lives in fx.pulseFlash, detonation loses its brightness punch, reduced detonation uses the field fade | none | `Assets/web/dtrh` | ported-by-merge |
| 6 | `20117b70f` | feat(dtrh): feel wave 2 base - every game moment is a dtrh:feel event | none | `Assets/web/dtrh` | ported-by-merge |
| 7 | `33c296db5` | feat(dtrh): feel wave 2 moments - phase events, countdown breath, ink banner, edge bloom, recap count-up | none | `Assets/web/dtrh` | ported-by-merge |
| 8 | `b1181bc99` | feat(dtrh): feel wave 2, field lane - the field answers holds, counts and streaks | none | `Assets/web/dtrh` | ported-by-merge |
| 9 | `41aa2c9ac` | feat(dtrh): feel wave 2, world lane - the tube warms and settles with the run | none | `Assets/web/dtrh` | ported-by-merge |
| 10 | `28eda3c39` | feat(dtrh): feel wave 2, audio lane - moments, phases and holds have a sound | none | `Assets/web/dtrh` | ported-by-merge |
| 11 | `36484d3b2` | feat(dtrh): feel wave 2, hub lane - the Warren breathes | none | `Assets/web/dtrh` | ported-by-merge |
| 12 | `c2312ba44` | feat(dtrh): feel wave 2 seams - the audio lane hears moments, phases and holds | none | `Assets/web/dtrh` | ported-by-merge |
| 13 | `820811b9d` | fix(dtrh): feel wave 2 review - a click is not a hold, one figure per streak pop, the melt does not end a hold | none | `Assets/web/dtrh` | ported-by-merge |
| 14 | `ce65441e6` | fix(dtrh): Escape only pauses the game, it never closes the window | `MainWindow/MainWindow.xaml.cs` (dtrhInFront joins GameClaimsEscapeAsPause, KeptEscapePage.Dtrh -> PostKeptEscape), `Services/Chaos/DtrhHostService.cs` (IsInFront, IsReady, PostKeptEscape), `Services/Safety/PanicPolicy.cs` | `CCP.Core/Services/Safety/PanicPolicy.cs` (KeptEscapePage.Dtrh + dtrh args, merged); page side (`engine/scene.js`, `exit.js`) in `Assets/web/dtrh` | ported-by-merge (Core rule + page) / ported-here (panel routing `MainShellWindow.PanicKey.cs` TryGamePauseOnEscape + `GameWindow.Dtrh.cs` PostKeptEscape, lane game-dtrh-host, test `GameWindowTests.Dtrh_EscapePauses_ASecondEscapeInsideTwoSecondsClosesIt`) |
| 15 | `02e141d0d` | fix(dtrh): emotes no longer fly to the counter (owner call); gold still flies | none | `Assets/web/dtrh` | ported-by-merge |
| 16 | `724ac82bc` | fix(dtrh): online media gets a copy on this machine so the tube can draw it | `Services/Chaos/DtrhAssetManifest.cs` (local copies of the remote pool, ccp.assets urls, re-post manifest, library steps aside at 8 copies), `Services/Chaos/DtrhHostService.cs` | none yet (`GameMediaManifest.cs` mirrors only the local library walk for the Back Room deal; online/mixed sources are not on this head) | needs-port (host exists since game-dtrh-host, local manifest only; the online pool copies + re-post land with lane game-dtrh-host-b) |
| 17 | `c59173385` | feat(dtrh): the bubbles' own pictures move | none | `Assets/web/dtrh` | ported-by-merge |
| 18 | `7860abdb2` | feat(dtrh): thread layers on the tube | none | `Assets/web/dtrh` | ported-by-merge |
| 19 | `e69254f33` | feat(dtrh): the Loom weaves the Descent's spirals | none | `Assets/web/dtrh` (Loom store already Core `DtrhLoomStore`) | ported-by-merge |
| 20 | `c591701b0` | feat(dtrh): pictures first in the tube, clips sparse later | none | `Assets/web/dtrh` | ported-by-merge |
| 21 | `c648d30b8` | feat(dtrh): the dock toys wake on their own | none | `Assets/web/dtrh` | ported-by-merge |
| 22 | `d86c3586a` | feat(dtrh): online gifs move, flash cards leave in style | `Services/Chaos/DtrhAssetManifest.cs` (loops lane: `RemoteEntry.IsLoop`, `loop:` name prefix, copy order loops > stills > clips) | none yet; page `hostMedia.js` in `Assets/web/dtrh` | needs-port (host manifest loops lane: lands with lane game-dtrh-host-b) |
| 23 | `4f4195f05` | fix(dtrh): flashes pick a moving picture whenever there is one; pause is a real pause in the app | none (page only: `engine/scene.js`, `engine/spawner.js`) | `Assets/web/dtrh` | ported-by-merge |
| 24 | `f96e84ed4` | fix(dtrh): review of the play-session work - panic still closes the game, leaks and a flicker closed | `MainWindow/MainWindow.xaml.cs` (unclaimed panic Escape closes the DTRH window: `DtrhHostService.CloseActive()`), `Services/Chaos/DtrhAssetManifest.cs` (loops-dry back-off 30 min) | page fixes in `Assets/web/dtrh`; panic close = PanicSurfaces "games" once a DTRH GameWindow exists | ported-by-merge (page leaks/flicker) / ported-here (panic close: PanicSurfaces "games" closes the DTRH GameWindow, lane game-dtrh-host) / needs-port (loops-dry back-off: lands with lane game-dtrh-host-b) |
| 25 | `d3bf87937` | feat(dtrh): no still pictures where a moving one can be had; flash bubble loses its dark core; quieter defaults | none | `Assets/web/dtrh` | ported-by-merge |
| 26 | `7db2f6c12` | feat(dtrh): chrome drops more often; material bubbles wear a ring of drifting motes | none | `Assets/web/dtrh` | ported-by-merge |
| 27 | `46e9e908d` | feat(dtrh): the fall gathers pace, junction tubes rush, toy wakes are seen, the guide's voice obeys its level | none | `Assets/web/dtrh` | ported-by-merge |
