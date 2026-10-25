# wire-shell-keyword-triggers

- Branch: `avalonia-port/wire-shell-keyword-triggers`; base `0e1aa0473`.
- Core now holds WPF's rolling typed buffer, whole-word/regex matching, preset-first collection, app-scope policy
  (`KeywordMatchRules.NeedsForeground` / `AppAllowed`), cooldown/mute expiry and the audio volume curve. WPF calls the same code.
- Avalonia: the shell constructor arms the runtime from saved settings; the Awareness master/keyboard switches match WPF
  `MainWindow.Awareness.cs:413-497`; the Windows hook is shared with the panic key and never installed in a
  `CCP_USERDATA_DIR` sandbox; panic (`PanicSurfaces.cs`) cuts the clip only.
- The foreground process is resolved only when the scope is not Everywhere or ignore-own-focus is on (WPF
  `KeywordTriggerService` :677-681).
- Turning triggers off detaches the key handler but keeps the hook: on this head the panic key owns it from boot
  (`Win32Input.Start`), which is the case WPF :426 keeps it for.
- The tutorial demo (`FireDemo`) is a no-op stub: WPF `FireDemoTrigger` (:394-424) is gated by `HasAccess` (#887) and
  fires a synthetic Highlight+AvatarComment trigger with no audio. Both actions arrive with `-b`.
- Row stays **stub**; follow-up **wire-shell-keyword-triggers-b**: Linux input source, clipboard/OCR scanner +
  confirmation, highlights/capture visibility, non-audio actions (including the demo), trigger-fired/pulse/recent-app
  feeds, full multi-action dispatch, source/entitlement lifecycle. Audio-only matching still consumes cooldowns when a
  trigger has only deferred actions.

## Tests

- Core `KeywordTextInputTests` (buffer, matching, cooldowns, scope, `ForegroundIsResolvedOnlyWhenScopeNeedsIt`).
- Avalonia `KeywordAudioRuntimeTests` (playback/volume, gates, preset precedence, repeat cancel, cooldown/mute, focus
  isolation, panic, boot arming, `TutorialDemoNeverPlaysTheSavedTriggerAudio`), `KeywordTriggersPanelTests`.

## Commands and results (remote, GitHub Actions)

- Fail-proof red, `FireDemo` playing saved audio: rcheck run 39706756965, `TutorialDemoNeverPlaysTheSavedTriggerAudio` FAIL.
- Fail-proof red, `NeedsForeground` always true: rcheck run 39707071202, `ForegroundIsResolvedOnlyWhenScopeNeedsIt` FAIL.
- Restored: rcheck run 39707464931 (Keyword* in Core + Avalonia tests) RCHECK-OK.
- Full gate: see `## Verification limits` in the push body (rgate run).

## Verification limits

- Windows hook / ToUnicodeEx / foreground resolution are compiled, not run; they need the batched live pass
  (evidence/live-queue). No render change, so no new PNG.
