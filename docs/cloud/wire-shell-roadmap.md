# wire-shell-roadmap: ledger row `shell-roadmap` (stub duplicate removed, row stays `wired`)

- Finding: the whole of WPF `MainWindow.Roadmap.cs` was already ported on the base
  (`QuestsTabView.Roadmap.cs`: header, lock overlay/reason, badge, node strip with photo/✓/note,
  🔒/📷 Fluent glyphs, 0.8 s pulse on the shared BeatLoop, locked-step gate, start -> picker ->
  confirm -> note -> SubmitPhoto -> repaint, stats strip; `MainShellWindow.Roadmap.cs`: the one
  RoadmapService, step popup, chime, Track Unlocked / Badge Earned messages). The ledger carried
  `shell-roadmap` twice: the `wired` row and a stale `stub` row ("node painting, photo submission
  and progress/event refresh are missing"), a leftover from a merge. Re-read against the WPF file line by line; no
  behaviour was missing, so this unit adds proof, not new UI.
- `QuestsTabView.ShowPhotoConfirmation` private -> internal (the leg after the OS file picker) so a
  headless test can drive it.
- New tests in `Tests/CCP.Avalonia.Tests/RoadmapNodeTests.cs`:
  - `PhotoConfirmGatesSubmitAndTheNoteLandsOnTheRepaintedNode`: "No" on RoadmapConfirmDialog ends
    the flow (no note dialog, step not completed); "Yes" + note -> SubmitPhoto -> RefreshRoadmapUI:
    step completed with the note, stats strip `N+1 / 21`, the node shows the `"note"` box.
    Fail-proven: dropping the confirm gate fails it (Assert.True), and so does dropping the
    post-submit RefreshRoadmapUI (strings differ).
  - `LockedTrackShowsTheOverlayAndItsReason`: Track 3 in a fresh profile shows the lock overlay,
    hides the node scroller, reason "Complete Track 2 Boss to unlock" (WPF :92-106).
- Ledger: the duplicate `stub` row for `shell-roadmap` is removed; the `wired` row's evidence lists
  the new tests. No other row touched.
- Panic: nothing new is opened. WPF panic does not close the roadmap dialogs or popup, so
  PanicSurfaces is unchanged.
- No trunk (`parity/trunk`) code imported; nothing to credit.

## Verification limits
- `dotnet build`: CCP.Core, CCP.Avalonia and every Linux test csproj in build.yml (Core, Core.Settings,
  Core.Content, Avalonia, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/
  Snapshot): 0 errors. The WPF projects were not built (Windows-only).
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter RoadmapNodeTests|PanicSurfaces|Quests`: 14/14 passed.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed. Both failures come from this container, and this diff
  does not touch Core: `LoopbackOAuthTests ... (host: "[::1]")` (no IPv6 loopback here) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the
  permission denial it relies on never happens).
- Not run: the OS file-picker leg (StorageProvider) and the live Track Unlocked / Badge message
  boxes. No Keincheck live run. The change is not visual, so `--render-all` was not run.
