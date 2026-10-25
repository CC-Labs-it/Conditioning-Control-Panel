- Ported WPF InboxNoticeCard into Avalonia: neutral styling, visible-owner/standalone fallback, single instance, optional primary and quiet choice, Escape and background drag. Actions close first; Serilog logs callback failures.
- Registered `inbox-notice` in PanicSurfaces and SafetyCritical; panic closes without executing a choice. The host implements the existing passive-startup marker.
- Adapted the pure TierOnNotice rule into Core and wired provider celebration checks and focus to post repeat tier rises through StartupLadder and the real Inbox flyout. First readings record silently; one-time celebrations take precedence.
- Added Core rule cases, headless lifecycle/action/panic/Inbox tests, parity rows and numbered tester steps.
- Provenance: fetched `parity/trunk` at `a4ee52d40`; it lacks InboxNoticeCard at the reference path, so no trunk files were imported. Adapted the base's WPF card (`a54ce18f5`) and PatreonActivation rule (file tip `fad6fe2c0`). The task's sync7 references `fad0ab21` / `b8f5c6a1e` identify the downstream lane this host unblocks.

## Verification limits

- Core suite: 3,961 passed, five existing Vosk tests skipped (model unavailable). Notice tests alone: 3/3 passed with the shipped font bootstrap. Combined headless run: 5 passed, 9 failed during glyph typeface creation when another class bootstrapped fonts first; no tests weakened or skipped.
- All 12 Linux workflow test projects built with zero errors, as did CCP.Core and CCP.Avalonia. Language and Snapshot retries passed after an MSBuild worker interruption / disk exhaustion; generated outputs were removed between builds. Existing warnings were not suppressed.
- Windows and real desktop ownership/drag were not run. No OS-specific APIs were added; window behavior uses Avalonia's platform implementation.
- Lapse/post-code posting and the authoritative server-answered drop signal remain for their lanes. The head conservatively retains the previous tier on cached drops (`ponytail` comment); Core covers both authoritative and cached drops.
- Assumptions: preserve WPF's synchronous non-modal UI-thread presentation; use a keyboard-accessible button for the quiet choice; omit the WPF emoji glyph from the new row.

Verification commands (Release; `DiffEngine_Disabled=true` for tests):

| Command | Exit / result tail |
|---|---|
| `dotnet build CCP.Core/CCP.Core.csproj -c Release`; `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors |
| `dotnet build <workflow Linux test csproj> -c Release` (single-worker retries as needed) | 0 for CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~InboxNoticeCardTests` | 0; 3 passed, 0 failed after restoration |
| Same notice tests after temporarily reverting close-before-primary, Inbox open and panic stop production lines | 1; 3 failed for the missing behaviors (not font errors); all changes restored |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~PanicSurfacesTests` | 0; 6 passed, 0 failed |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~TierOnNoticeTests` | 0; 8 passed |
| WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 1; logger IO failure after disk filled copying Windows VLC runtime assets; Windows tests not run |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` (final quiet run) | 0; 3,961 passed, five existing Vosk skips |
| `git diff --cached --check` | 0 |

The combined headless filter also included InboxFlyoutTests and PremiumCelebrationTests; its pre-existing font-bootstrap ordering failure is not represented as a passing run. `test-plan-coverage.sh` is absent from this base; new rows have numbered WPF-referenced tester steps manually. The Core rule uses the distinct name `TierOnNoticeRule` to preserve the frozen WPF head's public `TierOnNotice` type without assembly ambiguity.

An intermediate Core rerun under concurrent compilation failed the existing board-frame timing budget (13.32 ms); the final full suite passed after the builds ended. No test was weakened.
