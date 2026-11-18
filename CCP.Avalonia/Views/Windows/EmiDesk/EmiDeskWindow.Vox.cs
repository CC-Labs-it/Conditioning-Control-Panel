using System;
using System.Linq;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    internal sealed class EmiVoxOutput : IEmiVoxOutput
    {
        public bool IsOutputSuppressed => CoreAudio.IsOutputSuppressed;
        public int MasterVolume => CoreSettings.Current.MasterVolume;
        public Action? PlayOneShot(string path, float volume, string tag) =>
            CoreAudio.PlayStoppable(path, volume, tag);
    }

    public partial class EmiDeskWindow
    {
        internal IEmiVox? _vox;
        private string _voxMood = "idle";

        private void Vox(string? text)
        {
            try
            {
                _vox ??= new EmiVox(new EmiVoxOutput());
                if (string.IsNullOrEmpty(text)) { _vox.Stop(); return; }
                var t = text.Trim();
                if (t.Length is > 0 and <= 3 && t.All(c => c == '.')) { _vox.Tick(); return; }
                _vox.Speak(t, _voxMood);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox seam threw"); }
        }

        private void TearDownVox()
        {
            try { _vox?.Dispose(); _vox = null; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox teardown failed"); }
        }
    }
}
