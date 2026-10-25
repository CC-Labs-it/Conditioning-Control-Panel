# Wallpaper host — part-a

- Add Core WallpaperHost with serialized off-thread operations, first-original capture, immediate settings breadcrumb before writes, startup recovery, retry after failed restoration, and an exit drain that refuses later shuffles.
- Add a small platform seam: GNOME light/dark URIs and scaling, KDE per-screen image settings through bounded qdbus processes, and WPF's Win32 read/set plus asynchronous setting-change notification. Preserve original settings until restore succeeds.
- Wire Takeover's wallpaper action, replacement duration timer, Keep semantics, normal stop, Keep-off, panic (including SafetyCritical), and app shutdown. Sandboxed profiles do not initialize a real desktop backend.
- Add lifecycle, command-runner, pulse and panic tests. Consolidate the two touched parity IDs to one row each and add numbered manual checks; no rows are marked verified.
- Source: adapted the frozen WPF reference at base `39260f198fad67d90bc988c63a00480830e5fefc`. Read-only `parity/trunk` fetched at `a4ee52d40`; no trunk code imported, so there are no imported trunk commits to credit.
- Scope decision: the complete WPF remote-media pool and lifecycle, plus the platform host and tests, exceed this unit's line cap. This is part-a (local host plus callers). Remote wallpaper pool/credit and the empty-library warning remain follow-up work.

## Verification limits

- .NET 10: CCP.Core, CCP.Avalonia and all 12 Linux-compatible workflow test projects built with zero errors (single-worker MSBuild). Initial test namespace errors were fixed; a preliminary parallel build lost an MSBuild worker and passed on retry.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,956 passed, five existing Vosk-model tests skipped, zero failed. No tests were weakened or newly skipped.
- Focused `WallpaperHostTests` and `PanicSurfacesTests`: exit 0; 11 passed, zero skipped. Includes a real headless shell for pulse timing. The measured font problem did not reproduce in this subset.
- Final expanded filter `WallpaperHostTests|PanicSurfacesTests|TakeoverTests`: exit 0; 20 passed, zero skipped. This includes the solid-colour refusal and existing Takeover regressions.
- Failure proof: disabled only the production panic restore call, ran `PanicWallpaperEntryRestoresEvenWithoutShell`, and obtained exit 1 / Assert.True failure. Restored the call and reran the focused suite successfully.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. Test-plan coverage script is absent from this base; numbered steps were added anyway.
- The WPF test project also built on Linux with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. It was compiled only, never executed.
- Linux backend behavior is tested through injected process runners and temporary files. No live GNOME/KDE desktop was changed. Windows platform code compiled in the Avalonia head but was not run on Linux. No full Avalonia suite or live Windows UI tests were run.
- Portal-only desktops are refused: org.freedesktop.portal.Wallpaper has no original-wallpaper capture API. This follows WPF's safety rule to skip effects that cannot restore the desktop. GNOME requires both light and dark keys and usable local originals; unsupported desktops/slideshows remain unchanged.
- Linux snapshots use the existing WallpaperOriginalPath setting as an opaque JSON snapshot; Win32 keeps the WPF path representation. Failed/cross-platform restores retain their breadcrumb rather than capturing an overridden wallpaper as the original.
