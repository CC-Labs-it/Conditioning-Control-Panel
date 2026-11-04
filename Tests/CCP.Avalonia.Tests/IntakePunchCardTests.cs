using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class IntakePunchCardTests
{
    [Fact]
    public void DraftsAndRedemptionsSurviveReloadAndStopAtEight()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            IntakePunchCardState Read() => JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(Path.Combine(dir, IntakePunchCardState.FileName)))!;
            Assert.False(IntakePunchCardState.ReadEverCompletedIntake(dir));
            IntakePunchCardHead.Draft(dir, "a");
            IntakePunchCardHead.Draft(dir, "a");
            Assert.True(IntakePunchCardState.ReadEverCompletedIntake(dir));
            Assert.Single(Read().PendingDrafts);
            IntakePunchCardHead.Progress(dir, "a", 49.9);
            Assert.Equal(1, Read().PunchedCount);
            IntakePunchCardHead.Progress(dir, "a", 50);
            Assert.Equal(2, Read().PunchedCount);
            IntakePunchCardHead.Progress(dir, "a", 100, true);
            Assert.Equal(2, Read().PunchedCount);
            Assert.Empty(Read().PendingDrafts);
            for (var i = 0; i < 8; i++)
            {
                var id = i.ToString();
                IntakePunchCardHead.Draft(dir, id);
                IntakePunchCardHead.Progress(dir, id, 0, true);
            }
            Assert.Equal(8, Read().PunchedCount);
            Assert.NotNull(Read().CompletedUtc);
        }
        finally { Directory.Delete(dir, true); }
    }
}
