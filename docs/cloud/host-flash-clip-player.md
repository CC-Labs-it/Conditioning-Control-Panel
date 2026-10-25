- Part -a: port the silent looping flash host and wire warmed online clips into the normal Flash test/engine presentation path. Posters remain visible when admission is full; retry only while the flash is active.
- Adapt trunk `f3813bbf599a9ea868bc31f281329427a5fb421a` (frame delivery) and `0b13445f3b5014e80ee8677008d2cb05e7a7ea0a` (remote source), read from trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Current WPF wins for 8 total / 5 shared leases, oldest-leaving eviction, immediate release and bounded native teardown.
- Core owns admission, consent/source selection, ready/shown rotation and URL-hashed poster/clip files. Avalonia owns frame delivery and window lifetime. Panic invalidates pending bursts, cancels warming and clears the cache; a later explicit trigger can warm again.
- Register flash-clips in PanicSurfaces and SafetyCritical; add behavior tests and numbered tester steps.
- Remaining part -b: URL-keyed decoded-poster LRU and exact WPF remote file aging; voiced-flash sound/lifetime integration. ponytail: file cache retains up to 256 entries until panic instead of deleting clips under live decoders.

## Verification limits

Verification results are recorded below before pushing. Windows and native desktop playback cannot be exercised in this environment. The portable LibVLC path is compiled on Linux; no Windows-specific implementation was introduced.

- .NET SDK 10.0.100 installed; channel discovery returned 403, so the stable version was selected explicitly.
- `dotnet build -c Release`: Core, Avalonia, all 13 test projects named in the build workflow compiled with zero errors (including WPF with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`). Existing compiler/analyzer warnings remain. An initial overlapping Avalonia build hit a PDB lock; subsequent builds ran sequentially.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: 3,959 passed, 5 existing Vosk/model skips, 0 failed; exit 0. Earlier runs during compilation hit the unrelated board-frame timing threshold; its isolated rerun and the final full suite passed.
- Fail-proof: temporarily replaced the shared-cap assignment with total capacity; the admission test failed on the sixth shared player (exit 1). Restored the production line before the passing full suite.
- `bash .github/scripts/core-guards.sh`: all Core guards passed, exit 0. `git diff --check`: clean. The test-plan coverage script is absent on this base; numbered coverage was added manually.
- No live desktop/native VLC playback, paired remote feed, Windows runtime tests, or Vosk recognition was run. WPF was compiled only. No PR was opened.

Assumptions: the full decoded-poster LRU plus aging tests would exceed this unit's line cap, so this is part -a. Evicted leaving clips return to their poster before the player's bitmap is disposed. The existing portable LibVLC callbacks serve both operating systems; no additional OS seam is needed.
- Final `dotnet build Tests/CCP.Avalonia.Tests -c Release`: 0 errors, exit 0. `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~FlashClipHostTests|FullyQualifiedName~PanicSurfacesTests'`: 14 passed, 0 failed/skipped, exit 0; font creation worked on this base. The first panic scan correctly failed before adding the direct clip-host stop to the registry.
