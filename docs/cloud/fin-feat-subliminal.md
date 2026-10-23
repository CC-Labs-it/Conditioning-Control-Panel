# fin-feat-subliminal

- Match WPF whisper suppression now that this base actually hosts BarkEngine: playback's onStarted marks the duration plus a 250 ms tail, shorter clips cannot shrink the hold, and safety/guaranteed and possession/emergency text attribution keep their exemptions. Refused or muted clips never claim the floor. The old ledger's “no bark engine” note was stale.
- Offer and persist `Fredoka (bundled)` first, resolve the shipped font in the card and Advanced preview, trim stored names, discard extra family links, and preview the closed picker in its selected face. Reuse the shipped AppFonts resource.
- Send AI and remote custom words through the silent WPF custom path: blank rejection, trim, 200-character cap before tag removal, haptic anticipation, opacity override and 10 XP. A Stop during anticipation or the UI dispatch retires the pending card. Pool phrases retain their optional linked whisper; remote custom words retain their existing feature-use notification.
- Keep exactly one `feat-subliminal` ledger row, honestly `stub`; the remaining dependencies and behavior are below. Reuse the existing subliminal windows and panic teardown; no new surface or panic registration is introduced.
- No trunk code imported. `parity/trunk` was fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; all adaptations use the pinned base's WPF reference. No WPF source is modified.

## WPF audit and remaining gaps

Reference SHA: `aadc945328e54625be244b3f3f5504778b119f55`. Read `ConditioningControlPanel/Features/SubliminalFeatureControl.xaml` and `.xaml.cs`, then the service they drive, `ConditioningControlPanel/Services/Subliminal/SubliminalService.cs`, plus `Helpers/FontPickerHelper.cs`, `Dialogs/ColorEditorDialog.xaml.cs`, `Services/AudioService.cs` and the bark gate/subscriptions.

The settings panel already mirrors the live settings instance, persists the controls, rebinds after settings replacement, opens Messages/Advanced, tracks user-added/deleted mod defaults, and refreshes mod art. The ordinary scheduler, monitor selection, card look/envelope, focus option, linked audio lookup/volume, duck/unduck, audio stop, haptic lead, XP, Freeze/Reset and video-deferred Reset were already implemented on this base. The newly actionable gaps were bundled-font handling, whisper bark suppression and custom-word haptics/sanitization. These are addressed here.

Remaining WPF behavior, deliberately not labelled wired:

| WPF behavior | Evidence at the pinned SHA | Missing head dependency or integration |
| --- | --- | --- |
| Solid-mode cards on the shared NOACTIVATE host; one hold while running; per-screen replacement; Z=1200 above flashes; mixed-DPI placement; failed-host/off-canvas fallback; one-shot/throw cleanup and ref release | SubliminalService lines 724-890, EnsureHostRef/ReleaseHostRef; called live by ShowSubliminalVisuals lines 645-655 | `chaos-bubble-host` is `missing`; no Avalonia ChaosBubbleHostOverlay/shared canvas exists. A private subliminal-only host would not supply WPF's shared ownership or ordering. |
| Compositor registration, placements, most-recent layer card, clear on Stop/cancel and fallback through solid/classic paths; focus opt-in must stay classic | SubliminalService lines 629-642, 680-715, TearDownSurfaces/BlankCards; App.CompositorEnabled/App startup | Core SubliminalLayer exists, but no Avalonia CompositorEngine, RegisterLayer owner or per-monitor compositor hosts. This is part of `shell-clickthrough-overlays`, still `stub`. |
| Reused per-screen classic windows, blank/hide on Stop, close only on Dispose; active window handles for recurring topmost sweep | SubliminalService GetOrCreateScreenWindow, TearDownSurfaces, GetSubliminalWindowHandles and Dispose | The head currently creates/closes cards and raises them at show time. Shared topmost reconciliation is unported (`shell-clickthrough-overlays`). Pooling remains a local rendering gap alongside the host dependency. |
| Padded active-text OCR rectangles, including hosted/layer cards; invalidate cached exclusions on show | SubliminalService GetActiveTextScreenRects lines 1049-1139; App.xaml.cs lines 1588 and 1720 | The corresponding head OCR exclusion pipeline is absent (`shell-awareness`). |
| Authored hold/fade overrides, suppress-haptic option, selective one-shot cancellation while ambient subliminals keep running | FlashSubliminalCustom lines 307-321, StopOneShotSubliminals lines 192-208; live callers found in Deeper IActionDispatcher/EnhancementEngine/SpeakPromptSession, BackRoomFxServices and KeywordTriggerService | Those runtime adapters are unported; the reachable AI/remote custom path is addressed here, but does not expose authored envelopes or selective cancellation. |
| Linked audio without a card from trigger-mode avatar speech | PlayTriggerAudio lines 461-478; live caller AvatarTubeWindow.Speech.cs line 2133 | Trigger speech adapter in `avatar-tube-window` is not ported. |
| Per-display counter/event and its consumers: reactive bark, every-tenth SubliminalAck, profile statistics, remote screen-word stream; subliminal-start EMI moment | SubliminalService lines 610-612 and OnRunStateChanged; BarkService lines 747-748; AvatarTubeWindow.Reactions.cs lines 472-483; MainWindow.ProfileBubble.cs and RemoteControlService.Screen.cs | `feat-companion-barks`, `avatar-tube-window`, profile/remote screen and EMI integrations remain incomplete. Existing App.NoteFeatureUsed is schedule-based, not a replacement for the complete display event. |

These are live WPF paths. P122 does not apply: grep finds real callers, so neither render-all nor a dead-code exception could justify changing this row to wired. This is not a pure dependency NOOP: the base's active bark engine and bundled font made real fixes possible.

Reproduce the dependency/caller check without another branch checkout:

```sh
git grep -n -E 'ShowHostedSubliminal|ShowCompositorSubliminal|EnsureHostRef|ReleaseHostRef|GetActiveTextScreenRects' aadc945328e54625be244b3f3f5504778b119f55 -- ConditioningControlPanel/Services/Subliminal/SubliminalService.cs
git grep -n -E 'FlashSubliminalCustom|StopOneShotSubliminals|PlayTriggerAudio|SubliminalDisplayed' aadc945328e54625be244b3f3f5504778b119f55 -- ConditioningControlPanel
rg -n 'ChaosBubbleHostOverlay|CompositorEngine|RegisterLayer' CCP.Avalonia
```

The last command has no matches. `SubTextTransparent` is stored by both Advanced dialogs but WPF's card renderer does not read it; no new interpretation is introduced. Fluent icons follow the existing head decision. System font enumeration remains platform-specific; missing installed families use Arial and the platform's final fallback, while Fredoka is deterministic.

## Verification limits

Executed with .NET SDK 10.0.402 and `DiffEngine_Disabled=true`:

- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2,825 passed, five pre-existing Vosk tests skipped because the model is unavailable (the worker blocks its download). No tests were weakened or skipped by this change.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~SubliminalWhisperShowTests`: exit 0; eight passed, using fake playback and virtual haptics.
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed.
- Fail-before proofs: the audible-whisper test fails on the original implementation because onStarted is null; the custom-remote test fails on the original routing because it invokes the ordinary phrase callback; the bundled-font test fails against the three original font implementations because the first row is an installed system font instead of `Fredoka (bundled)`. Each test compiles and reaches its behavior assertion.

The initial multi-project build pass ran out of the worker's writable disk because every Avalonia test output copies the head's assets and native libraries. Only generated output directories were removed; remaining builds run sequentially and release each disposable output after recording the result. The first head-test pass exposed an incorrect new test assertion (`FontFamily.Name` returns the family name, not the resource URI); it was corrected to compare the FontFamily and still verify the actual Fredoka glyph typeface.

Final required Linux builds, each using `dotnet build <project> -c Release` (also `--nologo`, and `-nr:false` for the sequential pass):

| Project | Exit | Result tail |
| --- | --- | --- |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 errors |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 errors |

`dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~SubliminalWhisperShowTests|FullyQualifiedName~TextOverlayTests|FullyQualifiedName~SubliminalStealsFocusTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~MandatoryVideoSchedulerTests'`: exit 0; **25 passed, 0 failed, 0 skipped**. Unlike the worker's known bare-font bootstrap issue, these relevant classes can run with their existing shipped-font setup. The font assertions compare the exact bundled FontFamily resource and accept its actual glyph family metadata (`Fredoka Light` on Skia).

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; **0 errors**, 512 existing build/analyzer warnings; the WPF reference app and its tests compiled successfully on Linux. No WPF tests or Windows native paths are run. Other CI test projects were compiled, not executed; the full Avalonia suite and render-all were not run (P122 is not invoked).

Tests use isolated CCP_USERDATA_DIR profiles, fake audio, a manual clock and virtual haptic devices. No real desktop, account, audio device or user profile is exercised. Native Windows focus/click-through code can be compiled but cannot be run on this Linux worker.

## Assumptions

- The exact required commit `aadc945328e54625be244b3f3f5504778b119f55` governs the base; the runtime's local branch name is immaterial. The supplied checkout had a different commit and read-only git metadata, so the authorized independent-clone fallback was used.
- Do not invent a private replacement for the missing shared Chaos/compositor hosts or claim the whole row is wired. Prioritize the user-visible gaps with existing callers and retain the explicit remaining list.
- Preserve WPF's approximate audio-duration hold even after a clip is cut short; it expires by clock and only extends. Ignore invalid non-finite clip lengths rather than claiming an infinite hold.
