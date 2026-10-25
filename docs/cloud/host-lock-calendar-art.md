- Adapt WPF's calendar payload parser into Core CalendarArtData; reuse the existing Core LockCalendar and raffle day-mask logic. Sparse dates remain sparse, an explicit zero mask lights nothing, and program/legacy/default payloads retain WPF clamping.
- Register CalendarArtView on the existing Home billboard. The existing Chaster Event and Program Waiting cards now draw their calendar payloads and retain their existing CTA paths.
- Draw the seven-column sheet, counted-day entrance, lifted/ringed today, and counted/needed bar in Avalonia. Tint from the card; use the shared BeatLoop for ambient motion, hold when hidden, release on retirement, and park on panic through PanicSurfaces.
- Credit: sync7 WPF reference commit `ab9a293a946143afb3ef4f6ee96872324e5e15a1` (real counted-day banner masks), inspected through read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted current WPF reference behavior; existing Core/head files retained.
- Add focused Core payload/LockCalendar tests and a headless real Home caller test with rendered-pixel, hold/resume, release, and panic assertions. Add the parity row and numbered tester step.

## Verification limits

- .NET 10.0.100 used; the channel installer failed discovery with HTTP 403, so the SDK was installed by fixed version.
- Core and all 12 Linux-compatible test projects referenced by build.yml built successfully (zero errors; existing warnings).
- core-guards.sh passed. test-plan-coverage.sh is absent on the required base; a numbered WPF/Rows tester step was added.
- An initial concurrent Avalonia build hit an output-file lock; sequential validation replaces that attempt.
- Full Core rerun after restoring the mutation: **3957 passed, 5 Vosk tests skipped, zero failures** (exit 0). Vosk model is unavailable in this environment.
- Sparse-mask negative proof: replacing the production mask read with null yields **2 failed, 2 passed** (exit 1); production code restored.
- Headless CalendarArtTests + PanicSurfacesTests: **7 passed, zero failures** (exit 0), including actual rendering; the font limitation did not reproduce.
- Initial full Core run: 3956 passed, 5 Vosk tests skipped, one board frame-budget timing failure while builds were active. No tests were weakened.
- Initial head run caught a panic ordering error; the entry was moved after intake, preserving the existing first/last ordering contracts.
- The first WPF test cross-build exposed a Core/WPF CalendarInfo name collision; the new Core model is now BillboardCalendarInfo, preserving the frozen WPF sources.
- Final WPF test cross-build with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false: **zero errors, exit 0** (existing warnings). The WPF app also compiled.
- Final model-rename checks: **4 focused Core tests and 7 headless calendar/panic tests passed**; Core, Avalonia and all 12 Linux test projects rebuilt with zero errors; Core guards passed again.
- Windows desktop behavior cannot run on Linux. No new OS-specific code or platform seam is needed for this renderer.

## Assumptions

- LockCalendar is already shared on this base; reuse it rather than duplicating its lock-day parser.
- Keep the existing Event/Waiting providers and CTA routing as the real user paths.
- ponytail: defer decorative motes, twinkles, glints and progress sheen; retain the semantic calendar art and entrance timing.
- Panic additionally parks the new decorative loop; WPF leaves billboard decoration alone.
