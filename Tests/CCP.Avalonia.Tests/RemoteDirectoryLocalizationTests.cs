using System;
using System.IO;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class RemoteDirectoryLocalizationTests
{
    [Fact]
    public void RemoteDirectoryCopyUsesAllTenLanguages()
    {
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "CCP.Core", "CCP.Core.csproj"))) root = root.Parent;
            Assert.NotNull(root);
            foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
            {
                var resource = JObject.Parse(File.ReadAllText(Path.Combine(root.FullName, "CCP.Core", "Localization", "Languages", lang + ".json")));
                LocalizationManager.Instance.SetLanguage(lang);
                foreach (var (state, opted, claimed) in new[] { ("private", false, false), ("listed", true, false), ("claimed", true, true) })
                {
                    var key = "remote_directory_" + state;
                    var pill = MainShellWindow.DirectoryPillFor(true, opted, claimed);
                    Assert.False(string.IsNullOrWhiteSpace((string?)resource[key]));
                    Assert.False(string.IsNullOrWhiteSpace((string?)resource[key + "_tip"]));
                    Assert.Equal((string?)resource[key], pill.Text);
                    Assert.Equal((string?)resource[key + "_tip"], pill.Tip);
                    Assert.NotEqual(key, Loc.Get(key));
                    Assert.NotEqual(key + "_tip", Loc.Get(key + "_tip"));
                    Assert.Equal(Loc.Get(key), pill.Text);
                    Assert.Equal(Loc.Get(key + "_tip"), pill.Tip);
                    Assert.DoesNotContain('—', pill.Tip);
                    Assert.DoesNotContain('–', pill.Tip);
                    Assert.DoesNotContain('!', pill.Tip);
                }
            }
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); }
    }
}
