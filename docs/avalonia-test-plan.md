
# Spiral finishing steps

1. Open Studio Spiral Overlay and press THE LOOM twice. One window stays open. Save, overwrite and delete a spiral: rack thumbnails and the mounted Spiral library update; duplicate names without overwrite report an error. WPF `ConditioningControlPanel/Features/SpiralFeatureControl.xaml.cs`, `ConditioningControlPanel/Services/Chaos/LoomHostService.cs`. Rows: `feat-spiral`.
2. Switch mods while the Spiral card stays open: hero/side art and Default thumbnail follow the mod. Detach/reopen and repeat; no duplicate subscriptions. WPF `ConditioningControlPanel/Features/SpiralFeatureControl.xaml.cs` OnLoaded/OnUnloaded/OnModChanged/ApplyFeatureArt. Rows: `feat-spiral`.
3. Use a saved Loom tile folder action (portable head opens containing folder), then panic: Loom closes. Reopen: saved spirals remain. Without a web engine, WebHost displays the dependency panel. WPF `ConditioningControlPanel/Services/Chaos/LoomHostService.cs` Launch/Close/OnPageMessage. Rows: `feat-spiral`.
