# remix-core-a: move the Jackpot Remix rules to CCP.Core

Core move, unit A of `ConditioningControlPanel/Services/Remix`. Nothing in CCP.Core held these rules
before this unit, so every rule now has exactly one copy, in Core.

- `git mv` `Services/Remix/JackpotRemixPlan.cs` to `CCP.Core/Services/Remix/`. It was already pure.
  Only a doc `cref` to the WPF-only builder became plain text.
- `git mv` `Services/Remix/JackpotRemixDirector.cs` to `CCP.Core/Services/Remix/`. It holds the
  pure `JackpotRemixRoll`/`JackpotRemixRollState`/`JackpotRemixStep` and the stateful director (the
  carried roll, the one-file cache, quiet-window polling and the 10-minute retry delay). Its
  `App.*`/WPF dependencies are now constructor seams:
  - `owned` (was `PrizeGrants.IsGranted(PrizeGrants.JackpotRemix)`)
  - `motion` (was `MotionFx.Level`)
  - `builderDisabled` (was `JackpotRemixBuilder.Default.Disabled`; it is still checked after
    the other eligibility rules, so the builder is still created lazily at the same moment)
  - `build` (was `JackpotRemixBuilder.Default.BuildAsync(...)?.FilePath`)
  - `availablePhysicalBytes` (was the in-file Win32 `GlobalMemoryStatusEx` probe)
  - `App.Logger` became Serilog's static `Log`, which the WPF App points at `App.Logger`.
  - There is also an optional `odds` parameter, so tests can fix the 1-in-N roll. The heads leave it
    null and keep the default of 1 in 100, or the DEBUG `CCP_JACKPOT_ODDS` override.
- WPF: `FlashService.EnsureRemixDirector` passes those seams. The Win32 memory probe moved, unchanged,
  into `JackpotRemixBuilder.cs`, which stays in the WPF head (WebView2 host) together with the
  spike harness `JackpotRemixSpikeTests`. WPF behaviour is unchanged.
- Tests: `git mv` `JackpotRemixPlanTests` and `JackpotRemixRollTests` (both pure) to `Tests/CCP.Core.Tests`.
  New: `JackpotRemixDirectorTests`, which drives the director through each seam:
  - a hit plays a leftover file
  - not owned, motion Off or a dead builder never plays
  - a carried hit builds once through `build` and then plays the built file
  - memory pressure or a flash on screen holds the build
- Credits: no trunk import. `parity/trunk` has no Core move for Remix. The moved code was authored in
  `0cfe8cbb2` (plan) and `68527052a` (director), which are already in the base.
- No WPF source-scan test reads the moved files, so `SourceRoots` needed no change.

## Next units
- **remix-core-b**: Avalonia head wiring. An Avalonia `build` seam with a cross-platform remix host
  (the jackpot page under a headless browser or a native compositor), a cross-platform free-memory
  probe, and the director driven from the Avalonia flash path.
- **remix-core-c**: the Avalonia flash-side presentation (big and centred via
  `JackpotRemixRoll.CentredGeometry`, one XP-paying copy plus mirrors) and the Jackpot Remix toggle
  in the Avalonia flash settings, gated on the prize.

## Verification limits
- `dotnet build` (Release) succeeded for CCP.Core, CCP.Avalonia and the Linux test projects in
  `.github/workflows/build.yml`.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 1471 passed, 5 skipped, 2 failed. Both failures
  also fail on the untouched base in this container, so they come from the environment, not from this
  change:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` fails because the tests run
    as root, so a read-only folder is still deletable.
  - `LoopbackOAuthTests...(host: "[::1]")` fails because the container has no IPv6 loopback.
- Filtered `JackpotRemix` tests: 45 passed (Plan, Roll and the new Director tests).
- Compiled but not run: `Tests/ConditioningControlPanel.Tests` and the WPF head with it
  (`FlashService`, and `JackpotRemixBuilder` with the moved Win32 memory probe). Built with
  `-c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  and the build succeeded. WPF, WebView2 and Win32 code cannot run on Linux.
- No Avalonia UI changed, so no `Tests/CCP.Avalonia.Tests` classes are relevant to run.
