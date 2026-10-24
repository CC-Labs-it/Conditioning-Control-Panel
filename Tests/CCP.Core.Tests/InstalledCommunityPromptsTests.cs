using System;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class InstalledCommunityPromptsTests
{
    [Fact]
    public void Load_matches_installed_prompt_flags_and_handles_missing_or_corrupt_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "persona.json");
            Assert.Null(InstalledCommunityPrompts.Load(folder, "persona", "persona"));
            File.WriteAllText(path, "{\"Name\":\"Community persona\",\"PromptSettings\":{\"Personality\":\"be kind\"}}");
            var active = Assert.IsType<ConditioningControlPanel.Models.CommunityPrompt>(
                InstalledCommunityPrompts.Load(folder, "persona", "persona"));
            Assert.Equal("Community persona", active.Name);
            Assert.Equal("be kind", active.PromptSettings.Personality);
            Assert.True(active.IsInstalled);
            Assert.True(active.IsActive);
            Assert.False(InstalledCommunityPrompts.Load(folder, "persona", "other")!.IsActive);
            File.WriteAllText(path, "null");
            Assert.Null(InstalledCommunityPrompts.Load(folder, "persona", "persona"));
            File.WriteAllText(path, "{bad");
            Assert.Null(InstalledCommunityPrompts.Load(folder, "persona", "persona"));
            Assert.Null(InstalledCommunityPrompts.Load(folder, "../outside", null));
            Assert.Null(InstalledCommunityPrompts.Load(folder, "..\\outside", null));
        }
        finally { Directory.Delete(folder, true); }
    }
}
