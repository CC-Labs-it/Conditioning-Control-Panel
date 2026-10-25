- Restore Help and EMI book replay entry points. Each dismisses its panel before opening the same disposable show; panic still uses the existing `first-show` registration.
- Port the pure WPF EmiVox score/PCM synthesis into Core, without the unused desk player, typing tick, or persistent-cache implementation. Speech waits until EMI settles, uses idle/celebration moods, and adds WPF's 1.4-second speech gesture. Voice and cues replace their own channels independently and stop at disposal. Per-show voice cache is deleted. Master zero and output suppression stay silent.
- Resolve cues through the existing mod-audio seam before stock content. Add the Scrolller-only attribution mark, with the WPF 200 ms fade extended by Gentle visuals.
- Reuse native input regions for live bubble circles; the existing routed left press pops a bubble. Empty regions restore click-through at gather/verdict/guide. Reuse the X11 desktop Escape reader so focus on the speech card is unnecessary.
- Add Core voice tests and headless stage tests for replay ordering, voice settlement/cache/disposal/mute/mod cues, speech transforms, bubble input/pop/gather, Escape cleanup and attribution. Add numbered manual steps; keep the ledger `stub` with an explicit remaining list.
- Source: adapted from the frozen WPF reference at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. `parity/trunk` was fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported.

- The long-line Core test first failed with 80 blips (limit 13), exposing WPF’s fixed 240-iteration guard. Use a finite word/syllable-derived compression budget; keep the regression unchanged. Ordinary score/layout/tone rules remain the reference’s.

## WPF gap audit

At the pinned base, the current ledger's live gaps were silent EmiVox lines; Help/book replay absent; the stage could draw/pop bubbles but native click-through prevented clicks reaching them; drain was a weaker veil instead of blurred desktop capture; Linux Escape depended on focused speech. Reading the cited WPF files also found mod cue resolution, the speech bob/turn pulse and the Scrolller playback credit missing. These are implemented except for capture.

Still missing:
- `Services/Compositor/BrainDrainCapturePump.cs` and its desktop capture host: 767 reference lines before platform adaptation, tests and this unit's other work. It captures at quarter resolution every 40 ms; phase one is stationary blur, phase two moves only when motion is enabled. It requires platform capture, blur, current-frame ownership and shutdown, not a stronger opaque veil. Its safe port plus the completed gaps exceeds the 1,000-added-line budget. Current drain remains the deliberately lighter flat veil. Future publication must hand off immutable images, never mutable cross-thread SKBitmap instances.
- The WPF DEBUG `--first-show-preview` standalone launch. This does not block ordinary first-run or replay callers, but it is not ported and no full-parity claim is made.

The guide already has the timed hover and give-up paths. Its untested physical hover, composite screenshot placement, and lack of side-by-side evidence are verification limits, not absent callers. The native WPF first-show service has live first-run and replay callers, so P122 does not apply. Its `Assets/web/first-show` ledger citation is not the live native service implementation.

## Verification limits

- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 12 Linux test projects named in `.github/workflows/build.yml` passed (exit 0). Final changed head plus `Tests/CCP.Avalonia.Tests` compile: 0 errors (existing warnings remain).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,955 passed, 0 failed, 5 existing Vosk skips, 3,960 total. The new long-line test failed before the compression fix (80 blips), then passed unchanged.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” `test-plan-coverage.sh` is absent on the required base; numbered row coverage is added manually.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1`: exit 0; 305 existing warnings, 0 errors. Compiled only; Windows execution is not available here. The initial multi-node retry also hit MSB4166; the single-node retry passed.
- `git diff --check`: exit 0.

Initial parallel builds collided on shared Avalonia outputs; subsequent test-output copies exhausted disk. Generated outputs were cleaned and builds rerun sequentially, deleting copied test output between projects. No source or tests were removed to recover disk.

Avalonia window tests are compiled but not executed here, following the job's documented font-creation limitation; no fail-before-fix runtime claim is made for those tests. The supervisor must run `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~FirstShowStageTests` with `DiffEngine_Disabled=true` on the supported runner.

Native Windows/X11 input region dispatch, global Windows keyboard hooks, audible playback, online feed availability, real guide hover, mixed-DPI positioning, fade appearance and WPF visual comparison are not run in this headless environment. No real desktop/profile is opened. Windows code and the WPF test project compiled successfully but were not executed. Vosk model download is outside this unit and is documented as blocked by the job environment.

## Assumptions

- Keep the existing click-through refusal on unsupported native Wayland rather than opening a desktop-sized interactive stage. X11 Escape polling inherits the existing XWayland visibility limitation.
- Online first-show media comes from the existing Scrolller-only fetch path; bundled fallback media has no Scrolller credit.
- Reuse the existing Core audio and mod-resolution seams, stage/panic lifecycle, shared frame clock, and temporary download host. Do not introduce a second capture service or change the frozen WPF head.
