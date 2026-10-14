# wire-win-mantra: Mantra Lab stub -> wired

- **User path proven end to end.** The ledger's blocker ("the Avalonia Programs tab never builds
  `TodayTaskList`") was stale: programs-run-3b builds the today list and shows a Mantra task's door.
  `ProgramsRunControlsTests.LoadedRunThisHeadCannotFinishSaysSoAndOpensTheMantraDoor` now clicks that
  door and asserts the Mantra Lab opens at the task's reps (`TargetCount`, `/N` label) and that
  closing it ends the session.
- **StartMantraSession failure dialog ported** (WPF `MainWindow.PlayTab.cs:312-317`): a failed open
  is logged and shows WPF's "Mantras" / "Couldn't start the mantra session:\n\n{message}" through
  the existing `MessageDialog` (hardcoded English, as in WPF). This replaces the `ponytail:`
  logged-only shortcut. `MainShellWindow.NewMantraWindow` is a one-line seam so a test can make the
  open fail. Test: `MantraWindowSessionTests.FailedOpenShowsWpfsWarning`.
- **Ledger:** row `win-mantra` stub -> wired. Reach matches WPF's: only the `kept` program has
  Mantra tasks, and this head still refuses to enroll `kept` because KeywordTrigger is not raised
  (owned by shell-programs-tab). So the door appears today only for a `kept` run loaded from a
  Windows profile.
- No trunk (`parity/trunk`) commits were imported. Nothing there was needed.
- Panic: nothing new to register. The window's existing `mantra` PanicSurface still covers it.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter "MantraWindowSessionTests|ProgramsRunControlsTests"`:
  8/8 passed.
- Fail proofs: the dialog test fails with the `MessageDialog.ShowAsync` line removed
  (`Assert.Single() Failure: The collection was empty`). The door test fails with
  `StartMantraSession(reps)` removed from `OpenProgramMantrasAsync` (same assertion). Both were
  restored afterwards.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed. Both failures come from
  the container and this diff touches no Core code:
  - `LoopbackOAuthTests ... host "[::1]"`: the container has no IPv6 loopback.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: the tests run as root, so
    the folder can still be deleted.
- Builds with 0 errors: CCP.Core, CCP.Avalonia, every Linux test csproj in build.yml, and
  Tests/ConditioningControlPanel.Tests (with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`). The WPF tests compiled but were not run.
- Not run:
  - `--render-all`, because the Mantra window's visuals did not change. The only new UI is the
    reused MessageDialog.
  - Keincheck. The drone and tones were not heard live.
