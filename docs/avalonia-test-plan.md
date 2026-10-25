# Avalonia test plan

1. Open Mod Manager -> Create -> Mantras. Add duplicate phrases, blank cards and mismatched prompts. Export: IDs deduplicate, blank prompt falls back to phrase, blank cards vanish, invalid entries produce a skip warning. Toggle enabled, remove a card and reload. WPF `ConditioningControlPanel/Windows/ModCreatorWindow.Mantras.cs` Rows: `win-mod-creator`
2. Attach prompt/response mp3/wav audio and add retry/timeout lines. Export/load/re-export: clips and missing references survive, filenames deduplicate. Clear clips and Reset All: rows disappear. WPF `ConditioningControlPanel/Windows/ModCreatorWindow.Mantras.cs` Rows: `win-mod-creator`
3. Visit UI Art: all 34 paths and four groups match WPF order and dimensions. Browse/drop, export/load, clear and reset images. Frame editor remains deferred. WPF `ConditioningControlPanel/Windows/ModCreatorWindow.UiArt.cs` Rows: `win-mod-creator`
4. Preview twice to stop, switch clips, wait for natural end, then verify close/reset/panic terminate playback. Start Modding tutorial and move forward/back: section navigation precedes spotlighting. WPF `ConditioningControlPanel/Windows/ModCreatorWindow.xaml.cs` Rows: `win-mod-creator`
