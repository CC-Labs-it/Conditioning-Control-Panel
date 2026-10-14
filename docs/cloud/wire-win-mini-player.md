# wire-win-mini-player: Library opener for MiniPlayerWindow (row `win-mini-player` stub -> wired)

- The Library tab already opened `MiniPlayerWindow` from a thumbnail double-click and the context-menu
  Preview (WPF `MainWindow.Assets.cs` ThumbnailItem_Click / ThumbnailItem_Preview_Click / OpenAssetPreview).
  This branch adds the WPF behaviour it was still missing and the tests that prove the path.
- `AssetsTabView.OpenAssetPreview`: when the preview closes, the shell is re-activated only while our
  process still owns the foreground (WPF `previewWindow.Closed` with the GetForegroundWindow /
  GetWindowThreadProcessId pid check), so closing a preview never steals focus back from another app.
  The check is `AssetsTabView.AppInFront`: on Windows it reuses `EscapeClaim.CcpInFront` (now `internal`);
  elsewhere it asks whether one of our windows is active. It is read at Closing (ponytail comment:
  WPF reads after Closed; the foreground is the same at that moment).
- `AssetsTabView.LastPreview` exposes the opened preview for tests.
- Tests (`Tests/CCP.Avalonia.Tests/AssetsTabTests.cs`):
  - `DoubleClickingAThumbnailOpensItInTheMiniPlayer`: through the shell's Library door, the first click
    on a thumbnail toggles it and the second (ClickCount 2) opens the preview owned by the shell with the
    image shown; exactly one toggle (WPF order). The headless platform never reports ClickCount 2, so the
    second press is raised on the tile with its count.
  - `ContextMenuPreviewOpensAndClosingReactivatesTheShellOnlyWhileWeOwnTheForeground`: the context-menu
    Preview opens the preview without toggling; closing it activates the shell once when we own the
    foreground and zero times when we do not. Fail-proven both ways: re-activate removed -> Expected 1,
    Actual 0; guard removed -> Expected 0, Actual 1.
- Panic: WPF's panic does not close the preview (not in GameSurfaces; video plays `:no-audio`), so no
  PanicSurfaces line was added.
- Ledger: row `win-mini-player` -> wired with the evidence above; its stale duplicate row (older text,
  same id) was removed.
- Trunk: nothing imported. `parity/trunk` has the same opener without the re-activate; its pack-file
  preview (`b1423f1ea`, temp-decrypt + delete on close) comes with the Content Packs tree node, which this
  head does not build yet and belongs to the content-packs lane.

## Verification limits

- Built on Linux (.NET 10.0.112): CCP.Core, CCP.Avalonia and every Linux test project in build.yml
  (Core, Core.Settings, Core.Content, Avalonia, Avalonia.Consent/Language/CatalogueStartup/Scheduler/
  Report/GeneralSettings/Snapshot): all "Build succeeded".
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter AssetsTabTests|MiniPlayer|PanicSurfaces|EscapeClaim|PanicKey`:
  Passed 26, Skipped 1 (MiniPlayerVideoTests needs ffmpeg/libvlc, absent here), Failed 0.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed, both sandbox artifacts in code
  this branch does not touch (no Core changes): `LoopbackOAuthTests ... host "[::1]"` (no IPv6 loopback)
  and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the read-only
  folder is deletable).
- Compiled, not run: the Windows branch of `AppInFront` (`EscapeClaim.CcpInFront`, user32). Not run: a
  real-desktop double-click / Keincheck pass; `--render-all` (no visual change).
- WPF projects not built (Windows-only).
