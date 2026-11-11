using System;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

/// <summary>The Avalonia audio boundary for Core <see cref="EmiVox"/>, the twin of the WPF head's
/// EmiVoxOutput (App.Audio, App.Settings.MasterVolume and the one-shot handle's Stop).</summary>
internal sealed class EmiVoxOutput : IEmiVoxOutput
{
    public bool IsOutputSuppressed => CoreAudio.IsOutputSuppressed;
    public int MasterVolume => CoreSettings.Current?.MasterVolume ?? 0;
    public Action? PlayOneShot(string path, float volume, string tag) => CoreAudio.PlayStoppable(path, volume, tag);
}
