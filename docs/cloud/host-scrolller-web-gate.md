- Add the supported navigation fallback to the existing WebHost; Home browser direct-media navigation is a real caller. Source, Navigate and engine redirects share Core classification/policy.
- Route top-level media through the existing token-protected loopback WebAssetServer and Core device cache. Cache hits are free; fresh bodies use existing per-account byte charging, daily share, consent, expiry and incomplete-body cleanup. Responses support existing single-range seeking and use no-store.
- Apply honest UA and replace foreign Referer headers on Scrolller requests. Preserve caller header rules and original-URL navigation predicates.
- Register scrolller-web in PanicSurfaces and SafetyCritical: cancel fetches, invalidate URLs, terminate cached transfers and clear active direct-media hosts.
- Reference inspected: parity/trunk a4ee52d40; no trunk files imported. WPF ScrolllerWebGate was read as the specification. Related requested sync7 commits: f7102be55, c45d58a65, 7c2452f3e, 92fdebaa4, 0b4ae9a6e, 339609c5a, 0a77b1cef; no claim that these lanes are fully unblocked.

## Verification limits

- Avalonia.Controls.WebView 12.1.0 public API inspected by reflection. It has request headers but no public response/byte event, cancel or request deferral. Decompiled Windows WebViewCallbacks request callback applies headers only and discards edited URI/Method/Request. Transparent request redirection would silently fail there; this implementation uses navigation replacement instead.
- Embedded image/video/fetch/worker and API requests remain outside policy enforcement, charging and cache routing. The honest headers are best effort. No cross-platform DevTools meter, declared-length fallback, native browser-profile cache sweeps, refused-body daily blacklist, or WPF 2 MB open-range slice cap.
- Direct clips fetch whole (maximum 64 MB), paying for the downloaded whole file rather than bytes played. Existing Core cache may refuse to fetch when it cannot write; no uncached memory fallback.
- Official controls attribution in WPF; Enabled controls request access. No extra official-only restriction was invented.
- Linux headless checks are recorded below. Native Linux browser playback and Windows runtime behavior were not exercised; portable Windows-facing code is compiled with the Avalonia head. No new OS-specific implementation.
- Direct-media fallback also uses Core's existing 512 KB charge batches and download budget, rather than WPF's 256 KB batches and 20-second idle / 120-second total timers. Loopback ranges follow WebAssetServer semantics rather than WPF's 2 MB open-range / 64 MB explicit-range caps. The media resource context is unavailable, so extensionless media cannot be classified here.
- Initial checkout HEAD differed from the required base; its fetch failed because the checkout was read-only. The authorized fallback clone was reset to the exact expected base before work. No local uncommitted requester state was used.

### Completed checks

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0. Both also rebuilt through the test project references.
- Every additional Linux test project in `.github/workflows/build.yml` built Release with exit 0: CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. Generated outputs were cleaned between builds to avoid disk exhaustion.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; Failed 0, Passed 3953, Skipped 5 (existing Vosk model-dependent tests), Total 3958.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ScrolllerWebGateTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~WebAssetServerTests|FullyQualifiedName~WebHostSandboxTests'`: exit 0; Failed 0, Passed 23, Skipped 0. This builds the headless test project with zero errors. The environment supported these headless tests; no font failure occurred.
- Failure proof: temporarily remove the production `host.Source = null` panic teardown. `PanicClearsTheNavigatedHost` fails (exit 1, Assert.Null sees the media URL). Restore the exact file and the full focused run passes. An earlier redundant Core off-switch guard mutation remained green because the cache itself also enforces the switch; the recorded panic mutation independently proves the regression.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. `git diff --check`: exit 0. `test-plan-coverage.sh` is absent on this base, so its numbered WPF/Rows steps were checked directly.

### Assumptions

- Deliver the narrow navigation fallback while preserving `stub` status; do not claim embedded request interception or that all listed sync7 lanes are unblocked.
- Preserve WPF's Enabled-versus-Official distinction; Official is attribution metadata, not an extra access condition.
- Reuse the existing disk fetcher's consent and writeability requirements and existing local server rather than introducing an OS-specific proxy or private native-handle integration.
- Windows-targeted test build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1 --disable-build-servers -p:UseSharedCompilation=false`: exit 0; 557 warnings, 0 errors. The first attempt found a shared/WPF type-name collision (fixed by naming the shared helper ScrolllerNavigationGate); a subsequent parallel retry hit MSB4166. The serialized final build passed. Windows tests were not run.
- Cold-cache regression: persist a cached picture, reset the in-memory book and use production asynchronous warm-up while away. Before the fix the focused test fails (exit 1, Assert.NotNull at the replay). The helper now awaits the index before deciding whether fresh fetches are required; cancellation during warm-up returns a refusal.
