# astra-fin2-chaos-overlay-window

- Re-port the intent of reviewed patch `33f5186d1b503ad632002b54a479e33448fb9844` onto base `ffaaaf6635bdae78794c8504a7df9d6def37048a`. The base still had empty countdown input hooks.
- Restore WPF any-key and left/right mouse countdown skipping, without consuming input. Windows opts into any-key handling on the existing scoped listener; X11 uses the existing XInput2 panic listener. Native hooks stay disabled in sandbox profiles.
- Completion and close release input subscriptions. Generation checks reject stale queued presses; duplicate events complete once. Existing run-owned panic teardown is retained.
- Keep the supplied full/restart/ready lifecycle and platform-filter tests, adding the base's bundled-font setup. Tests use the existing isolated profile and an injected listener.
- Consolidate the base's three duplicate ledger rows into one, retain their evidence text, prepend the new countdown evidence, and add a numbered manual tester step. Status remains `stub`; unrelated chaos lanes are unchanged.
- WPF reference: `ConditioningControlPanel/Chaos/ChaosOverlayWindow.xaml.cs`, countdown methods at the pinned base. No trunk code imported: `git fetch origin parity/trunk` failed because credentials were unavailable, so no fetched trunk commit is claimed.

## Verification limits

SDK: .NET 10.0.100; Release builds use `-m:1`; tests use `DiffEngine_Disabled=true`.

- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references).
- `test-plan-coverage.sh`: unavailable on this base (exit 127); a numbered tester step was added anyway.
- Initial focused `ChaosCountdownInputTests` run: exit 0, 9 passed.
- Negative control: temporarily disabled countdown subscription and X11 delivery, and restored Escape-only Windows handling. The same tests exited 1 with 6 failed / 3 passed; implementation restored immediately afterward. No test assertions were weakened.
- Core suite: `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0, 4409 passed, 0 failed, 5 existing Vosk-model-dependent skips.
- Final filtered `ChaosCountdownInputTests|ChaosRunHostTests` run: exit 0, 26 passed, 0 failed, 0 skipped.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors; compiled only, not run on Linux.
- All Linux workflow test projects plus CCP.Core and CCP.Avalonia built successfully (exit 0, zero errors). This includes CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, the main Avalonia tests, Core.Content, Core.Settings, and Core.Tests.

 Native Windows hooks compile on Linux but cannot be exercised here. Synthetic X11 event routing does not establish native desktop delivery; that needs a nested desktop. No tests use the real desktop or real user profile. Native Wayland cannot provide global skip through XInput2; XWayland observation reaches X clients only.

## Assumptions

- WPF left/right presses are authoritative; middle/wheel/releases do not skip.
- Reuse the already-running X11 listener rather than adding another native listener.
- Preserve all base ledger evidence, treating prior countdown absence claims as historical rather than replacing newer chaos-lane claims with the old patch's missing-feature list.
