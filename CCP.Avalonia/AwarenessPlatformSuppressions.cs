using System.Diagnostics.CodeAnalysis;

// This linked WPF watcher is constructed only by the Windows 10.0.17763 guard in App.Awareness.cs.
[assembly: SuppressMessage("Interoperability", "CA1416", Scope = "member", Target = "~M:ConditioningControlPanel.Services.Awareness.SmtcMediaWatcher.InitializeAsync", Justification = "The Avalonia factory selects SMTC only on supported Windows versions.")]
[assembly: SuppressMessage("Interoperability", "CA1416", Scope = "member", Target = "~M:ConditioningControlPanel.Services.Awareness.SmtcMediaWatcher.ReadAsync", Justification = "The Avalonia factory selects SMTC only on supported Windows versions.")]
