# wire-win-media-history: Media Log stub -> wired

Ledger row `win-media-history` goes from **stub** to **wired**. The Media Log (Assets tab > Media Log)
now does the three things WPF's window did that this head did not.

- **Reveal selects the file** (WPF `Helpers/ExplorerLauncher.RevealInExplorer`). New `ExternalOpener.Reveal`
  in `CCP.Avalonia/Platform/ExternalOpener.cs` (the head's one launch seam, so SandboxNet still applies):
  - Windows: `explorer.exe /select,"<path>"`.
  - macOS: `open -R <path>`.
  - Linux: freedesktop `FileManager1.ShowItems` through `dbus-send --print-reply`.
  - Fallbacks: the folder opens when the file is gone (#998), when the desktop has no FileManager1
    service, or when `dbus-send` is missing.
  - Test seam: `SelectInFileManager`.
- **One copy of the Explorer command line**: `CCP.Core/Helpers/RevealArguments.cs`. It holds the #1108
  quoting rule (the switch stays outside the quotes and a path with a quote is refused) plus the
  dbus-send arguments (file URI, commas escaped). WPF `ExplorerLauncher.BuildRevealArguments` and
  `BuildFolderArguments` now call it; the old WPF bodies are removed.
- **Load preview** (WPF `BtnPreviewLoad` / `PreviewLoad_Click` / `ShowRemotePreview(fetch: true)`):
  - Shown only for an online still, and only with the app-wide remote-media consent
    (`MediaSource != "local" && HasRemoteMediaConsent`). The click handler checks consent again.
  - Fetches into memory (this head has no RemoteMediaCache; same 24 MB cap as the first-show
    download), then decodes at 720 px off the UI thread.
  - A generation guard drops a result whose row was deselected meanwhile.
  - On failure it shows `label_no_preview` (bytes came but could not be read) or streamed-only, and
    the retry button comes back while consent allows it.
  - Test seam: `MediaHistoryWindow.FetchRemote`.
- **MediaFailed plate**: `MediaHistoryPreviewRules.VideoLooksBroken` (Core) returns true for an empty
  file, bytes that sniff as a non-video, or a strict container (mp4/m4v/webm/mkv/avi/flv) with no video
  head. The pane then shows `label_file_not_found` before LibVLC runs. LibVLC raised no error for an
  unreadable mp4 and painted a blank pane. Old `.mov` and formats the sniffer cannot name
  (`.wmv`, `.mpg`, `.3gp`) are still left to the player.
- No panic change: WPF's panic does not close the Media Log, and this unit adds no new surface.
- No trunk code imported: `parity/trunk`'s `MediaHistoryWindow` is behind this base (emoji glyphs, no
  thumbnails/audio differences beyond what is already here). The Audio type was folded earlier from
  `a464a897c`.
- Left outside this row: Brain Drain audio entries need a Brain Drain clip player that calls
  `RecordAudio` (WPF `BrainDrainService`), and this head does not have one yet. The window already
  lists, filters and previews Audio rows from the shared log.

Added lines vs `origin/avalonia-port/quiz-core-a`: 456 (cap 1000).

## Verification limits

- `dotnet build`: no errors for CCP.Core, CCP.Avalonia, and every Linux test csproj in
  `.github/workflows/build.yml`: CCP.Core.Tests, Core.Settings, Core.Content, Avalonia.Consent,
  Language, CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot and CCP.Avalonia.Tests.
  `Tests/ConditioningControlPanel.Tests` also builds with `-p:EnableWindowsTargeting=true`.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2780 passed, 6 skipped, 2 failed. The 2 failures are
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the container runs as root, so a
  read-only folder still deletes) and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...("[::1]")` (no
  IPv6 loopback here). Both fail the same way on the untouched base.
- Focused Core runs: `RevealArgumentsTests` (3 new), `MediaHistory*`, `SandboxNetTests`
  (`EveryLaunchGoesThroughExternalOpener` still passes) and `MediaTypeSniffer*` all pass.
- `dotnet test Tests/CCP.Avalonia.Tests --filter MediaHistoryWindowTests|PanicSurfaces|AssetsTabTests`:
  14/14 passed. The new fact is
  `MediaLog_reveals_the_file_and_loads_an_online_still_only_with_consent`. Against the base, the new
  tests do not compile: the seams and members they drive (`SelectInFileManager`, `FetchRemote`,
  `LoadPreviewAsync`, `VideoLooksBroken`, `BtnPreviewLoad`) are absent.
- `dotnet run --project CCP.Avalonia -- --render-all <dir>`: 221/221 views rendered.
  `MediaHistoryWindow.png` shows the default state; Load preview is hidden until an online still is
  selected with consent, as in WPF.
- Compiled, not run:
  - The Windows `explorer.exe /select` branch and the macOS `open -R` branch of
    `ExternalOpener.SelectNative`.
  - The real `dbus-send` call (the test drives the seam; no session bus in the container).
  - WPF `ExplorerRevealArgumentTests`: they build but cannot run on Linux (no
    Microsoft.WindowsDesktop.App). `RevealArgumentsTests` pins the same strings in Core.
- Not verified live: a real online fetch (the test serves bytes through `FetchRemote`; the sandbox
  refuses network), and LibVLC playback (no libvlc in this container).
