using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubMusicTests
{
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Last;
        public bool Disposed;
        public double Volume { set => Last = value; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Menu_fades_mute_and_retargets_master_then_releases()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var priorAudio = LibVlcAudio.Instance;
            LibVlcAudio.Instance = null;
            var settings = CoreSettings.Current;
            int oldMaster = settings.MasterVolume;
            bool oldMute = settings.ChaosMenuMusicMuted;
            ChaosHubWindow? hub = null;
            try
            {
                settings.MasterVolume = 100;
                settings.ChaosMenuMusicMuted = false;
                hub = new ChaosHubWindow();
                var voice = new Voice();
                hub.MenuMusicPlay = (path, volume, loop, ended) =>
                {
                    Assert.EndsWith("menu_theme.mp3", path);
                    Assert.Equal(0, volume);
                    Assert.True(loop);
                    return voice;
                };
                hub.StartMenuMusic();
                for (int i = 0; i < 20; i++) hub.MenuMusicTick();
                Assert.Equal(0.25, voice.Last, 5);
                settings.MasterVolume = 50;
                for (int i = 0; i < 20; i++) hub.MenuMusicTick();
                Assert.Equal(0.25, voice.Last, 5);
                hub.FindControl<Button>("BtnMenuMute")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(settings.ChaosMenuMusicMuted);
                for (int i = 0; i < 12; i++) hub.MenuMusicTick();
                Assert.Equal(0, voice.Last, 5);
                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
                try { hub.Show(); hub.Close(); }
                finally { global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false; }
                Assert.True(voice.Disposed);
                var field = typeof(ConditioningControlPanel.Models.AppSettings).GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var handlers = (Delegate?)field!.GetValue(settings);
                Assert.DoesNotContain(handlers?.GetInvocationList() ?? Array.Empty<Delegate>(), d => ReferenceEquals(d.Target, hub));
                settings.MasterVolume = 100;
                Assert.Equal(0, voice.Last, 5);
            }
            finally
            {
                hub?.DisposeMenuMusic();
                settings.MasterVolume = oldMaster;
                settings.ChaosMenuMusicMuted = oldMute;
                LibVlcAudio.Instance = priorAudio;
            }
        });
    }

    [Fact]
    public void Leaving_menu_fades_to_zero_and_releases_voice()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var priorAudio = LibVlcAudio.Instance;
            LibVlcAudio.Instance = null;
            var hub = new ChaosHubWindow();
            var voice = new Voice();
            try
            {
                hub.MenuMusicPlay = (_, _, _, _) => voice;
                hub.StartMenuMusic();
                for (int i = 0; i < 40; i++) hub.MenuMusicTick();
                hub.FindControl<Grid>("MenuView")!.IsVisible = false;
                for (int i = 0; i < 39; i++) hub.MenuMusicTick();
                Assert.False(voice.Disposed);
                hub.MenuMusicTick();
                Assert.True(voice.Disposed);
                Assert.Equal(0, voice.Last, 5);
            }
            finally { hub.DisposeMenuMusic(); LibVlcAudio.Instance = priorAudio; }
        });
    }
}
