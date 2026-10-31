using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ReleaseButtonCopyTests
{
    [Fact]
    public void Release_button_has_short_copy_in_all_ten_shipped_languages()
    {
        var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages"), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var text = (string?)JObject.Parse(File.ReadAllText(file))["btn_v7_2_0_is_out"];
            Assert.False(string.IsNullOrWhiteSpace(text), file);
            Assert.Contains("7.2.0", text!);
            Assert.DoesNotContain('!', text!);
            Assert.DoesNotContain('\u2013', text!);
            Assert.DoesNotContain('\u2014', text!);
        }
    }
}
