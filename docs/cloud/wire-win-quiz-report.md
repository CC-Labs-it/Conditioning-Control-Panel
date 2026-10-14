# test(avalonia): quiz report rows hover and open through the gate (win-quiz-report)

Ledger row `win-quiz-report` stays **stub**. Re-reading the WPF reference showed there is nothing
left to port: `QuizReportWindow` and its only opener, the past-quizzes list
(`MainWindow.Lab.cs:468-580`), are already ported. The row cannot honestly become **wired**,
because WPF itself never shows that opener: `RefreshPastQuizzes` bails while `BtnStartQuiz` is
hidden, and WPF keeps it hidden on purpose (`GradedIntakeTabView.xaml:139`, "pending removal").
Showing it only on Avalonia would add a trigger WPF does not ship, which the brief forbids.

- `QuizPastRunsReportTests` now covers the one behaviour the ledger listed as "styled but
  untested", the run-row hover (WPF Lab.cs:567-574). Through an open intake gate (WPF
  Lab.cs:370-374), a row starts Transparent, turns `#15FFFFFF` under a headless pointer and goes
  back to Transparent when the pointer leaves.
- The test now opens the first report with a real pointer click instead of a raised
  `Button.ClickEvent`. The old event was raised on a row inside the disabled launch zone, the
  NeedsLogin state that the sandbox defaults to. That passed, but it could never prove a user
  can click the row.
- Checked panic: WPF panic (`MainWindow.WindowChrome.cs:281-283`) closes only `QuizWindow` and
  `PopQuizWindow`, never `QuizReportWindow`, so I added no `PanicSurfaces` entry.
- Ledger: updated row `win-quiz-report` with the new evidence. It also names follow-up lane
  `wire-win-quiz-report-b`: a product decision to either show `BtnStartQuiz` or remove the classic
  quiz on both heads.
- No trunk commits imported. `origin/parity/trunk` holds the same port (`b0335f105`, `2886855de`)
  that is already on this base.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter QuizPastRunsReportTests`: 1/1 passed.
  Fail-proof: with the `#15FFFFFF` setter removed from `GradedIntakeTabView.axaml` the test fails
  (`Expected #15ffffff, Actual White`). The setter was then restored.
- `dotnet build` of CCP.Core, CCP.Avalonia and every Linux test project in build.yml (Core.Settings,
  Core.Content, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot,
  Avalonia.Tests): all built.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed. Both failures come from
  this container, not from this diff, which touches no Core code:
  `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` fails because the container
  has no IPv6 loopback ("No data available ([::1])"), and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` fails because the tests run as
  root, so the folder lock the test sets up does not stop the delete.
- No UI change, so no `--render-all` run.
- WPF projects were neither built nor run (Windows only). No WPF code changed.
- No live run: the list cannot be reached in the UI on either head.
