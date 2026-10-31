using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF AudioService.cs:833 stops the previous sound even when the next path is missing.
public sealed class SoundReplacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacement_stops_previous_sound_before_play_or_invalid_path_completion(bool missing)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-sound-replace-");
        var valid = Path.Combine(dir.FullName, "clip.mp3");
        File.WriteAllText(valid, "fixture");
        var events = new List<string>();
        var ends = new List<Action?>();
        var audio = new LibVlcAudio((_, _, _, _, ended, followMaster) =>
        {
            Assert.True(followMaster);
            var id = ends.Count;
            ends.Add(ended);
            events.Add("play " + id);
            return () => events.Add("stop " + id); // Native completion arrives later.
        });
        try
        {
            audio.PlaySound(valid, 1f, "first", null, null);
            audio.PlaySound(missing ? Path.Combine(dir.FullName, "missing.mp3") : valid,
                1f, "replacement", null, () => events.Add("finished replacement"));
            Assert.Equal(missing
                ? new[] { "play 0", "stop 0", "finished replacement" }
                : new[] { "play 0", "stop 0", "play 1" }, events);
            ends[0]?.Invoke(); // A late old completion must not discard the new stop.
            audio.StopSounds();
            if (!missing) Assert.Equal("stop 1", events[^1]);
            var count = events.Count;
            audio.StopSounds();
            Assert.Equal(count, events.Count);
        }
        finally { audio.StopSounds(); dir.Delete(true); }
    }
}
