# fin-feat-flash

- Base verified: `c1bf80a368ed69d6c16e37f1282b7c8867120cbe`; independent cloud snapshot was clean. Work is limited to `avalonia-port/cloud-fin-feat-flash`.
- Add GIF/animated WebP playback to ordinary and hydra flashes: bounded, display-sized, composed frames; restore-previous disposal; ceiling stride over the clip; WPF speed clamp and 10 ms playback floor; static fallback. Mutable Skia bitmaps remain on the decoding worker; only copied Avalonia bitmaps leave it.
- Reuse the existing shared flash clock for frames and Pendulum. Reuse Core motion, monitor-aware pivot/phase separation, reduced/off rules, rounded-corner sizing, and drag/tap/place/fling physics. The clock ends when its last animated/moving flash closes.
- Add owned Pendulum, Rounded corners and Draggable controls in WPF order; preserve synced but unowned picks; refresh ownership-driven rows on grant changes. Bind both mod-aware feature-art plates with the existing head helper.
- Close active and exiting flashes and retire queued spawns on every service Stop, including idle one-shot bursts. Panic already reaches this flash lifecycle through the registered `engine` surface; no new surface is introduced.
- Replace repeated historical ledger text with one honest `stub` row and the current remaining gaps. Keep every existing test and add focused decoder, playback, rendering, input, ownership and cleanup checks.
- Reference/adaptation credits (read-only `parity/trunk`, pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`): WPF `Services/Media/AnimatedWebp.cs` at `0560a6dda238a4e21c973a3bd1d006d93ac8002a` supplies the composed-frame/budget/disposal algorithm; WPF flash source is present in `0c6d2cba67ecab048dff3161bb6ef23cd5f1bfac`; the existing Core physics moved with `012c9449e41a533b253c3d4d1299f90674a045a0` are reused. No existing head file was overwritten by an imported file.

## WPF comparison and scope

Read both ledger-cited WPF feature files in full, then followed their live service calls into `Services/Flash/FlashService.cs`, `Services/Media/AnimatedWebp.cs`, the compositor and the existing Core rules. Historical ledger claims about missing DND, content-pack images, clicks, hydra, XP, glow and stay/exit were already obsolete on this base.

Actual gaps at the start of this unit:

- Flash sound selection/playback, ducking, sound-length lifetime and sound-sensitive XP.
- GIF/animated WebP playback, including speed and disposal rules.
- Owned Pendulum rendering and motion choice; rounded corners and draggable options/runtime behaviour.
- Shatter dismissal and its priority over the leave picker/note; Jackpot Remix generation/director and option.
- Remote still/GIF/video-clip pools and actionable empty-library source offers.
- Gaze pop, linger and dwell visuals; avatar pre-announce and click reactions.
- Lucky glow pulse/toast and Natasha favourite treatment.
- Solid rendering (the existing toggle only persists a setting).
- Mod-aware hero/side art and ownership-event rebuild.
- Stop closing current bursts and dropping pending bursts instead of letting them finish.

This unit implements playback, Pendulum, rounded corners, dragging, art, row rebuilding and Stop cleanup. The other gaps remain explicitly listed on `feat-flash`; it is not marked wired. Remote delivery, audio orchestration and the remaining effects require additional implementation and behaviour coverage beyond this unit's 1,000-added-line budget. Server-delivered ownership remains the separate `svc-prize-ownership` dependency; Debug/test ownership keeps the existing seam. These are live WPF paths, so P122 does not apply and this is not a dependency-only NOOP.

## Verification limits

Commands use Release and `DiffEngine_Disabled=true` for tests. Every required Linux test csproj from `.github/workflows/build.yml` was built explicitly, along with Core, the shipped head and the WPF test compile attempt.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`, `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build Tests/<project>/<project>.csproj -c Release`: exit 0, zero errors for each of `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests`.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows code compiled; not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0; “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0; 3,542 passed, 5 skipped, 0 failed. The five existing skips require the unavailable Vosk model. The first full run had one remote-media failure (`Loops` versus `Clips`); those source/test files are unchanged. That test and the 14 flash scheduler tests passed in isolation (15/15), then the full repeat passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FlashFinishTests|FullyQualifiedName~FlashDriftTests|FullyQualifiedName~FlashPopTests|FullyQualifiedName~FlashWireTests|FullyQualifiedName~FlashPickTests|FullyQualifiedName~FlashOverlayPlacementTests'`: first run exit 1, 30 passed/5 font failures. After final additions the combined run stalled after font errors and its testhost was terminated. No green combined result is claimed.
- Final isolated `FlashFinishTests` with `--no-build --logger 'console;verbosity=detailed'`: exit 1, 9 passed/2 font failures. GIF composition/speed/looping, Pendulum state/render bounds, rounded geometry, drag/fling/tap, Stop and panic passed. The two feature-card layout tests stopped at Inter glyph creation before their assertions.
- Final focused filter covering the six flash classes above, excluding only the five named font-dependent card-layout cases at invocation: exit 0; 31 passed, 0 skipped, 0 failed. All cases remain present and enabled in source; this subset does not replace GitHub's complete run.
- `git diff --check`: exit 0. Added content was checked for personal paths and email addresses; none found.

- Regression proof: the owned-Pendulum test failed against the original head (`Expected: Pendulum; Actual: null`). The Stop regression failed with the old cleanup behaviour temporarily restored (`Expected: 2; Actual: 0`); the implementation was restored immediately.
- The cloud's known Inter glyph-typeface failure prevents card-layout assertions from completing. Tests remain enabled for the supervisor's GitHub run; no test is weakened or skipped to conceal it.
- Headless tests use the existing isolated `CCP_USERDATA_DIR` setup and stub overlay presentation. They do not touch the real desktop or real profile.
- No Windows desktop execution, live X11 compositor/input/DPI verification, audible playback or Vosk model download is claimed. Existing Windows platform code is compiled through the Avalonia head; no new OS-specific code is introduced.

## Assumptions

- Preserve the existing head-only Test button and its point-fired timing.
- Reuse the current per-flash window renderer and shared clock; defer a full-screen compositor/solid-host rewrite.
- Retain the existing server ownership boundary rather than granting prizes locally.
- Prefer the user-visible playback/motion/input/art/Stop gaps within the line cap; leave the row stub for all remaining live WPF behaviour.
