# wire-win-help-video: HelpVideoWindow plays its tutorial clip (row `win-help-video` stub -> wired)

- `CCP.Avalonia/Views/Windows/HelpVideoWindow.axaml.cs`: ported WPF `StartClip` playback. A topic with
  `ClipFile` and no drawn loop plays `<base>/Resources/tutorial_videos/<ClipFile>` muted (`:no-audio`,
  WPF `LibVlcSilence.NoAudioOption`) and looping (WPF `EndReached` -> UI thread -> `Stop`/`Play`).
  WPF's `VideoView` becomes the head's existing video path: `VlcFrameSink` into an `Image` in
  `VideoContainer` (as `MiniPlayerWindow` / `BubbleCountWindow`); `VideoService.SharedLibVLC` becomes
  `LibVlcAudio.Shared`. Fail-soft branches as WPF: no LibVLC (Warning), missing file, or a start
  exception -> surface hidden, what-it-does fallback, never throws. `OnClosed` stops and disposes the
  player and media and frees the frame buffer, as WPF's `MiniPlayerWindow`-derived teardown.
- A drawn help loop still wins over a clip (WPF `TryShowLoop`); every shipped topic has a loop or no
  clip, so shipped users see the same as WPF. The clip path is reached through the same callers as
  WPF (ModCreator, Session Editor ?, WebcamCalibration ?).
- Panic: WPF's panic never closes the help window, so nothing is added to `PanicSurfaces`.
- `Tests/CCP.Avalonia.Tests/HelpVideoWindowTests.cs`: new
  `ClipTopicPlaysTheClipMutedAndLoopingAndFreesItOnClose` (real 2 s / 24-frame clip on the system
  libvlc: frames reach the `Image`, playback loops past the clip's end, no audio track, close frees
  the sink). Skips only without libvlc, and never on Linux CI (same guard as `BubbleCountGameTests`).
- `docs/avalonia-parity.md`: row `win-help-video` -> wired (both lines of the duplicated row; the
  older one marked superseded).
- No `parity/trunk` commit touches HelpVideoWindow; nothing imported.

## Verification limits

- Fail-proven: the new test is red before the port (VideoContainer hidden) and red with the loop's
  re-`Play` commented out (33 frames, never passes the clip's end).
- `dotnet test Tests/CCP.Avalonia.Tests --filter "HelpVideoWindowTests|HelpButtonsTests|SessionEditor|ModCreator|WebcamCalibration|BubbleCountGameTests|HelpLoop|ClipArtView"`:
  54 passed, 0 failed. `HelpVideoWindowTests` alone run 3x: 4/4 each time.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed - both environmental and in
  code this branch does not touch (no CCP.Core change): `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...`
  (container runs as root, so the delete is never refused) and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...(host: "[::1]")`
  (no IPv6 loopback in the container).
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- Built (0 errors): CCP.Core, CCP.Avalonia, and the Linux test projects from build.yml (Core.Tests,
  Core.Settings.Tests, Core.Content.Tests, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/
  GeneralSettings/Snapshot.Tests, Avalonia.Tests). WPF projects not built (Windows only).
- Compiled, not run: Windows (bundled libvlc under `publish\libvlc\`). No WPF side-by-side (P35).
  WebcamCalibration's ? path still not live-run (needs a webcam).
- `--render-all` not run: the render sample topic (FlashImages) has a loop, so the window's render is
  unchanged; no shipped topic sets `ClipFile`.
- .NET SDK came from Ubuntu's `dotnet-sdk-10.0` (10.0.112) because dot.net / builds.dotnet.microsoft.com
  were blocked by the session's egress proxy.
