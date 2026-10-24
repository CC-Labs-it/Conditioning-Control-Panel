# sync7-port-audio-enhancement-bridge

- Audited WPF reference commits `4df31260e14a6f94042007a1eaeadaf2dbeec261`,
  `1320982c3bd27f224835f3c48a4841ecf2c6e72e`,
  `c186f22624720120216c9446f3722352f1b88dcb`, and
  `39e8ba8341d4364b688c91a42162b0a9488c9630` against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- Recorded missing host dependencies below. No production code imported from
  `parity/trunk` (fetched read-only); no sibling playlist/player implementation added.
- Preserved the sync ledgers and parity document. No new surface, panic registration,
  ledger promotion, or tester step is needed for this documentation-only audit.

## Commit disposition

### `4df31260e` — blocked-by-host

The playlist bridge needs `IAudioPlaylistService`, its track/state/position events,
and the playlist service startup owner. None exists in CCP.Avalonia or CCP.Core
on this base. The existing Deeper player also explicitly lacks its audio transport
(`CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs:654`) and refuses audio
play (`:783`). `DeeperLocalAudio` is the editor's separate preview transport,
not a playlist service (`CCP.Avalonia/Views/Deeper/DeeperLocalAudio.cs:10`).

Once that host lands, adapt the WPF track-pinned time source and separate enhancement
host: unbind on track replacement or stop, retain on pause, defer first bind until
playing, cache misses per track, and yield to the Deeper player on position ticks.
Do not add a dead bridge or a new single-implementation interface ahead of its caller.
No behaviour test added: there is no callable playlist surface on this base.

### `1320982c3` — blocked-by-host; settings/copy already-covered

Already covered: the default-on, JSON-persisted `AudioPlaylistEnhancements` property
is in `CCP.Core/Models/AppSettings.cs:9911`; English switch/tooltip copy is in
`CCP.Core/Localization/Languages/en.json:3769` (the other eight translations are
also already in Core). No changes to those files.

Missing: `AudioPlaylistCard`/model, playlist service, and installed bridge. Thus the
immediate switch refresh/unbind, options-fold state, and engine Stop's playlist stop
cannot be connected. The existing shell stop entry point is
`CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:89`; adding a no-op stop
call would not implement the reference. Start must never start the playlist.
No new behaviour or test claimed for the pre-existing setting/copy.

### `c186f2262` — blocked-by-host

The stopped/detached transport guard requires the absent playlist time source.
Device-error propagation requires the absent `EnhancementAudioPlayer` and playlist
service/card. Mid-drag cancellation requires the absent playlist card/model. Preserve
all three when those hosts land: stopped or detached sources cannot seek/pause/play,
a device failure publishes a stop to the card, and a replaced track cancels scrubbing.

The Goon portion also lacks a host/director: the head documents Goon's disabled
entry point at `CCP.Avalonia/Views/Windows/MainShellWindow.PlayTab.cs:8`.
Stop-all, panic fallback, haptics panic and graceful host close must close the toy
director (refuse later cues until reopened), not merely stop its current cue.
No new surface to register in PanicSurfaces and no production behaviour to test here.

### `39e8ba834` — blocked-by-host

`EnhancementAudioPlayer` is absent; the existing player's volume callback explicitly
has no transport to write (`CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs:885`).
Do not route this control to the application mixer. The future transport must apply
sample gain, initialized before playback and cleared at stop, rather than setting
Windows WaveOut volume. The editor-only LibVLC transport is not that player.

Goon's host and page integration are absent in this head, as above. Its remaining
requirements are: no practice toy cues; signed cap can only lower the player's own
cap (default 0.7); local slider alone may raise that cap; heartbeat silence over six
seconds stops the current cue before the existing twenty-second recovery. Frozen
WPF web assets were not edited to simulate a working Avalonia host.

## Assumptions

- Missing playlist/player/Goon hosts belong to sibling lanes. The explicit
  blocked-by-host instruction takes precedence over constructing those hosts here.
- Tests proving a production-line reversion are inapplicable when no production
  behaviour changes. Existing regression suites are validation only, not evidence
  that the four blocked ports are implemented.

## Verification limits

- SDK: .NET `10.0.402`. Channel discovery encountered a 403 on the CI feed;
  installing the released version from the builds feed succeeded (exit 0).
- `dotnet build <project> -c Release --nologo`: exit 0, zero errors for both
  `CCP.Core` and `CCP.Avalonia`, plus every Linux test project named in
  `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`,
  `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`,
  `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`,
  `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and
  `CCP.Avalonia.Snapshot.Tests`. Existing compiler/analyzer/XAML warnings remain.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --no-restore`: exit 0; **3953 passed, 0 failed, 5 skipped**
  (3958 total, 40 seconds). The existing Vosk tests skip without the model;
  the environment reports its model download blocked. No tests changed or disabled.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. An initial
  read-only sandbox attempt could not create temporary files; the rerun with
  temporary-file access passed.
- `test-plan-coverage.sh` is absent on this exact base (attempt exits 127).
  No ledger rows were added or promoted.
- Avalonia tests, including the nearby `DeeperHubTests`, were compiled but not run:
  the task documents font-creation failures on this cloud base and directs
  compile-only validation until the font lane lands. No font-test bootstrap fix
  is present in this snapshot. GitHub must run the relevant headless tests.
- No runtime checks on Windows, audio devices, or the missing playlist/Goon hosts.
  No new production line exists to revert for a red/green proof.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  557 warnings, **0 errors**. WPF application and test assembly compiled;
  neither was run on Linux.
- `git diff --check`: exit 0. Added-line count versus the exact base: 112 (cap 1000).
