using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class ImportProgressTextTests
{
    private static string Fmt(int c, int t, string f) => $"{c} / {t} - {f}";

    [Fact]
    public void Before_the_first_report_the_bar_is_indeterminate_and_the_count_is_empty()
    {
        Assert.True(ImportProgressText.IsIndeterminate(null));
        Assert.True(ImportProgressText.IsIndeterminate(new ImportProgress(0, 0, "")));
        Assert.Equal("", ImportProgressText.CountLine(null, Fmt));
        Assert.Equal(0, ImportProgressText.Percent(null));
    }

    [Fact]
    public void A_report_fills_the_bar_and_names_the_file()
    {
        var p = new ImportProgress(37, 412, "file.mp4");
        Assert.False(ImportProgressText.IsIndeterminate(p));
        Assert.InRange(ImportProgressText.Percent(p), 8.9, 9.0);
        Assert.Equal("37 / 412 - file.mp4", ImportProgressText.CountLine(p, Fmt));
    }

    [Fact]
    public void An_overshoot_never_passes_the_total()
    {
        var p = new ImportProgress(9, 4, "x.png");
        Assert.Equal(100, ImportProgressText.Percent(p));
        Assert.Equal("4 / 4 - x.png", ImportProgressText.CountLine(p, Fmt));
    }

    [Fact]
    public void A_long_file_name_keeps_its_extension()
    {
        var name = new string('a', 80) + ".mp4";
        var s = ImportProgressText.ShortFileName(name);
        Assert.Equal(ImportProgressText.MaxFileChars, s.Length);
        Assert.EndsWith("....mp4", s);
    }
}
