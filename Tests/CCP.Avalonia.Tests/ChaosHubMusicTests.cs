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
    private sealed class Voice : LibVlcAudio.IPausableVoice
    {
        public double Last;
        public bool Disposed, Paused;
        public void SetPaused(bool paused) => Paused = paused;
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
    public void Leaving_menu_pauses_and_return_resumes_the_same_voice()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var priorAudio = LibVlcAudio.Instance;
            LibVlcAudio.Instance = null;
            var hub = new ChaosHubWindow();
            var voice = new Voice();
            try
            {
                int starts = 0;
                hub.MenuMusicPlay = (_, _, _, _) => { starts++; return voice; };
                hub.StartMenuMusic();
                for (int i = 0; i < 40; i++) hub.MenuMusicTick();
                hub.FindControl<Grid>("MenuView")!.IsVisible = false;
                for (int i = 0; i < 39; i++) hub.MenuMusicTick();
                Assert.False(voice.Disposed);
                Assert.False(voice.Paused);
                hub.MenuMusicTick();
                Assert.True(voice.Paused);
                Assert.False(voice.Disposed);
                Assert.Equal(0, voice.Last, 5);
                hub.FindControl<Grid>("MenuView")!.IsVisible = true;
                Assert.False(voice.Paused);
                Assert.Equal(1, starts);
                for (int i = 0; i < 40; i++) hub.MenuMusicTick();
                Assert.True(voice.Last > 0);
                hub.FindControl<Grid>("MenuView")!.IsVisible = false;
                for (int i = 0; i < 10; i++) hub.MenuMusicTick();
                hub.FindControl<Grid>("MenuView")!.IsVisible = true;
                for (int i = 0; i < 40; i++) hub.MenuMusicTick();
                Assert.False(voice.Paused);
                Assert.Equal(1, starts);
                hub.DisposeMenuMusic();
                Assert.True(voice.Disposed);
            }
            finally { hub.DisposeMenuMusic(); LibVlcAudio.Instance = priorAudio; }
        });
    }
}
