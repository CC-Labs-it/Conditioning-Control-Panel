# Avalonia tester steps

1. Refresh Settings → Audio and dashboard output pickers. On Windows confirm active render endpoints follow System default. Choose a device and hear one-shots, layers and quiz drone there; unplug it and check fallback. WPF `Services/AudioService.cs:EnumerateOutputDevices`, `ApplyPreferredDevice`. Rows: `shell-audio-playback-devices`.
2. Open Graded Intake → Quiz with drone off, then on. Confirm looping, neutral mod cue preference over legacy bundled cue, silence when neither exists, master gain, surrender mute/restore, and close/panic stopping the drone. WPF `Windows/QuizWindow.xaml.cs:StartDrone`, `EnterSurrenderMode`, `ExitSurrenderMode`, `OnClosed`. Rows: `shell-audio-playback-devices`.
