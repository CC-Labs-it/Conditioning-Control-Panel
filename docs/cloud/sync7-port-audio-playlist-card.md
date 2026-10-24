# sync7-port-audio-playlist-card

- Audited WPF commits `ad963d9f1c98271a78705a4c97bbc94a19651755`,
  `ab5f591723b29c0e5fe2d97e92b662342782041d` and
  `e4648fa4f14dd761dfecb7fa006fc3f6c1cde777` against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- No production code imported. Read-only `parity/trunk` was fetched; no trunk
  implementation was adapted, so there are no imported trunk commits to credit.
- Preserved the sync ledgers and parity document. No new surface or ledger status
  was added; PanicSurfaces registration and tester-step coverage are not applicable.

## Commit dispositions

| WPF commit | Disposition | Missing host and evidence |
| --- | --- | --- |
| `ad963d9f1` | blocked-by-host | The card requires `IAudioPlaylistService` (tracks, transport, options and UI-thread events). Neither that contract nor `AudioPlaylistService` exists in Core/Avalonia. `CCP.Avalonia/Views/Tabs/DeeperTabView.axaml:480` proceeds directly to the library card without a playlist card. Settings alone (`CCP.Core/Models/AppSettings.cs:9879`) do not supply a player. Building the missing service would port sibling commits outside this unit. |
| `ab5f59172` | blocked-by-host | Removes `.ogg` from the playlist card's extension list and picker filter. Core/Avalonia have no `AudioPlaylistCardModel` or playlist file picker to correct. Applying this to other audio pickers would change unrelated features. |
| `e4648fa4f` | blocked-by-host | Both targets are absent: the playlist seek slider and `EnhancementAudioPlayer` playback-stopped handler. `CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs:654` explicitly stubs audio loading, and line 1026 stubs audio seek. `CCP.Avalonia/Views/Deeper/DeeperLocalAudio.cs:48` instead consumes LibVLC EndReached for the editor; it is not the missing shared playlist/Deeper player and has no MediaFoundation byte-count heuristic to fix. |

The WPF behavior to carry forward once the sibling host lands is: the self-contained
card between hero and library, service-driven rows and transport, four supported
file types (`.mp3`, `.wav`, `.flac`, `.m4a`), immediate groove-click seek and one seek
on thumb release. A clean playback stop with a loaded file is natural completion;
a device error counts only within the last `max(1024, bytesPerSecond / 2)` bytes.
Explicit stop unhooks the completion handler first. Tests must cover these rules
against the eventual host, including the measured MP3 ending 1055 bytes short.

## Verification limits

Validation results are recorded below. The required .NET SDK is 10.0.402. This branch changes documentation only;
there is no new behavior to test or production line to revert for a red/green proof.
Windows playback and WPF desktop behavior cannot run on this Linux worker.

- `dotnet build -c Release` for CCP.Core, CCP.Avalonia and all 12 Linux
  test projects referenced by `.github/workflows/build.yml`: exit 0, 0 errors.
  This includes Core.Tests, Core.Settings.Tests, Core.Content.Tests and Avalonia's
  main, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report,
  Scheduler and Snapshot suites. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references, 1611 app types, 2151 Core types). An initial unprivileged invocation
  could not create temporary files; the rerun with writable temporary storage passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`:
  initial exit 1 (3952 passed, 5 skipped, one failure in
  `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`).
  The isolated diagnostic rerun passed (exit 0, 1 test). A full `--no-build` rerun
  passed (exit 0, 3953 passed, 5 skipped, 3958 total). No tests were changed.
- Avalonia suites were compiled but not run: no new behavior or relevant new test
  class, and this worker has the stated font-creation limitation. No desktop or
  notification tests were run against a real profile.
- `test-plan-coverage.sh` is absent on this base; no ledger rows were promoted.
- WPF test-project cross-build with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors,
  557 warnings. Compiled only; WPF tests and Windows audio were not run.
- `git diff --check`: exit 0. Public-path/identity scan of this report: no matches.
- No Vosk model download or Windows runtime checks were performed.

## Assumptions

- The instruction to report missing hosts takes precedence over implementing sibling
  audio-service/player lanes. An inactive card with a null service would not satisfy
  the requested behavior, so none was introduced.
- Existing playlist settings and localized strings do not constitute an operational
  playlist host. All three commits remain blocked rather than already-covered.
