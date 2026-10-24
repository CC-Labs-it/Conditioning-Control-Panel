# sync7-port-companion-tube-alpha

- Ported WPF `877085a515f42e92e5492244d3ccb88ac9d47479`: clear the complete premultiplied pixel for alpha 1–8, preserving alpha 0 and alpha 9–255. Shared rule: `CCP.Core/AvatarTube/TubeArtAlpha.cs:9`; focused tests: `Tests/CCP.Core.Tests/TubeArtAlphaTests.cs:9` (all 256 alpha values, idempotence, custom floor, null/empty input).
- Apply the pass before both attached and detached tube art is painted at `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.axaml.cs:966`. Adapter: `CCP.Avalonia/Helpers/TubeArtAlpha.cs:18`; weak caching, original instance for clean/non-bitmap art, original fallback with Serilog debug logging on processing errors. No new surface; existing `PanicSurfaces.cs:84` tube registration remains applicable.
- Headless mod-art integration: `Tests/CCP.Avalonia.Tests/TubeArtAlphaTests.cs:22`, covering both styles, source preservation, cache identity, null, clean-image identity and the alpha 8/9 boundary. Numbered manual step in `docs/avalonia-test-plan.md`.
- Source credit: adapted the listed WPF commit directly. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk implementation imported and no sibling commits ported. No sync ledgers or parity ledger changed.

## Verification limits

- SDK: .NET 10.0.100. Channel discovery returned HTTP 403; the explicit SDK version installed successfully.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 12 Linux-compatible test projects named in `.github/workflows/build.yml` passed with zero errors. Existing warnings remain. An initial overlapping head build hit an output-file lock; the sequential rebuild passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~TubeArtAlphaTests`: exit 0, 2 passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~TubeArtAlphaTests`: exit 0, 1 passed using bundled font setup and the assembly's isolated user-data profile.
- Negative proof: temporarily restored the old production assignment (`_imgTubeFrame.Source = art`), then ran the same head test with rebuilding enabled. Exit 1, pixel comparison failed at test line 60 because haze remained. Restored the production scrub call afterward.
- Full Core suite initial run: exit 1, 3,954 passed, 5 skipped (Vosk model unavailable), 1 failed: the unrelated `BoardTilesTests.A_frame_fits_the_budget` measured 12.65 ms under concurrent compilation. Retry after Avalonia compilation: exit 1, the same counts and the same timing test failed (22.63 ms); WPF cross-compilation was still active. No tests were changed or disabled.
- Restored production head-test rerun: exit 0, 1 passed (0 failed), confirming the negative mutation was removed. WPF cross-build was attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it stalled and was cancelled, then retried with `-p:UseSharedCompilation=false -m:1 -nr:false`. That attempt compiled the temporary WPF assembly with warnings but did not finish after approximately six minutes and was cancelled too. The WPF test project has no completed build result here, and Windows code/tests were not run. Final Core run without concurrent builds: exit 1, 3,954 passed, 5 Vosk skips, the same board timing failure at 13.79 ms versus its 12 ms budget. Full Core gate remains red; the new alpha tests pass.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; this base does not contain the script. No ledger rows were added or promoted; a numbered tester step was still added.
- Native Windows click-through and actual desktop input were not run on Linux. Headless verification checks pixels and both style-loading paths; it cannot prove OS input routing.

## Assumptions

- Use the existing tube surface and retain the WPF alpha floor of 8 for all tube styles, including attached-only mod fallback.
- Keep bitmap decoding and caching in the Avalonia head; Core owns only the pixel rule.
- Native desktop click-through must be checked on a real desktop; headless tests prove the painted pixel bytes and production wiring.
