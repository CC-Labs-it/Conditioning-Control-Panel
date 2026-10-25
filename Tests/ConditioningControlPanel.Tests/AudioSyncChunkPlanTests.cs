using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1475: audio-sync haptics stopped at 30:00 because the chunk plan was fixed at
/// 30 minutes when the page sent no length. The plan now grows on demand.
/// </summary>
public class AudioSyncChunkPlanTests
{
    private static List<AudioChunk> Plan(int count, int seconds)
    {
        var list = new List<AudioChunk>();
        for (int i = 0; i < count; i++)
            list.Add(new AudioChunk
            {
                Index = i,
                StartTime = TimeSpan.FromSeconds(i * seconds),
                EndTime = TimeSpan.FromSeconds((i + 1) * seconds),
                State = ChunkState.Ready
            });
        return list;
    }

    [Fact]
    public void Playback_past_thirty_minutes_appends_grid_chunks()
    {
        var chunks = Plan(6, 300); // the old 30 min fallback
        var added = ChunkManager.ExtendPlan(chunks, 7, 300);

        Assert.Equal(2, added);
        Assert.Equal(8, chunks.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), chunks[6].StartTime);
        Assert.Equal(TimeSpan.FromMinutes(35), chunks[6].EndTime);
        Assert.Equal(TimeSpan.FromMinutes(40), chunks[7].EndTime);
        Assert.Equal(7, chunks[7].Index);
        Assert.Equal(ChunkState.NotStarted, chunks[7].State);
    }

    [Fact]
    public void Target_inside_plan_changes_nothing()
    {
        var chunks = Plan(6, 300);
        Assert.Equal(0, ChunkManager.ExtendPlan(chunks, 5, 300));
        Assert.Equal(6, chunks.Count);
    }

    [Fact]
    public void Short_unstarted_last_chunk_widens_to_its_slot()
    {
        var chunks = Plan(2, 300);
        chunks[1].State = ChunkState.NotStarted;
        chunks[1].EndTime = TimeSpan.FromSeconds(420); // a known length that was too short

        ChunkManager.ExtendPlan(chunks, 2, 300);

        Assert.Equal(TimeSpan.FromSeconds(600), chunks[1].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(600), chunks[2].StartTime);
    }

    [Fact]
    public void Short_ready_last_chunk_is_left_alone()
    {
        var chunks = Plan(2, 300);
        chunks[1].EndTime = TimeSpan.FromSeconds(420);

        ChunkManager.ExtendPlan(chunks, 2, 300);

        Assert.Equal(TimeSpan.FromSeconds(420), chunks[1].EndTime);
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void Empty_plan_grows_from_zero()
    {
        var chunks = new List<AudioChunk>();
        Assert.Equal(1, ChunkManager.ExtendPlan(chunks, 0, 300));
        Assert.Equal(TimeSpan.Zero, chunks[0].StartTime);
    }

    [Theory]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\",\"duration\":2712.48}", 2712.48)]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\",\"duration\":3600}", 3600)]
    public void Reported_duration_is_read(string message, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), AudioSyncService.ParseReportedDuration(message));
    }

    [Theory]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\",\"duration\":0}")]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\",\"duration\":null}")]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\"}")]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"u\",\"duration\":999999}")]
    public void Missing_or_nonsense_duration_is_null(string message)
    {
        Assert.Null(AudioSyncService.ParseReportedDuration(message));
    }
}
