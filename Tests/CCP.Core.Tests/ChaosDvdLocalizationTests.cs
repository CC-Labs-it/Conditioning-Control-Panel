using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

[Collection(GlyphFallbackLanguageCollection.Name)]
public sealed class ChaosDvdLocalizationTests
{
    // WPF ChaosModeService.cs:2731,2733 supplies the English wording.
    [Theory]
    [InlineData("en", "Porn DVD", "📀 now loading")]
    [InlineData("de", "Porno-DVD", "📀 wird geladen")]
    [InlineData("es", "DVD porno", "📀 cargando")]
    [InlineData("fr", "DVD porno", "📀 chargement en cours")]
    [InlineData("it", "DVD porno", "📀 caricamento in corso")]
    [InlineData("ja", "ポルノDVD", "📀 読み込み中")]
    [InlineData("ko", "포르노 DVD", "📀 로딩 중")]
    [InlineData("pt-BR", "DVD pornô", "📀 carregando")]
    [InlineData("ru", "Порно-DVD", "📀 загрузка")]
    [InlineData("zh-CN", "色情DVD", "📀 正在加载")]
    public void Dvd_copy_is_shipped_and_resolves_in_the_selected_language(string language, string banner, string loading)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Localization", "Languages", language + ".json")));
        Assert.Equal(banner, json.RootElement.GetProperty("chaos_dvd_banner").GetString());
        Assert.Equal(loading, json.RootElement.GetProperty("chaos_dvd_loading").GetString());
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            Assert.Equal(banner, Loc.Get("chaos_dvd_banner"));
            Assert.Equal(loading, Loc.Get("chaos_dvd_loading"));
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void Dvd_user_path_looks_up_both_strings_instead_of_embedding_English()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "Views", "Chaos", "ChaosRunHost.cs"));
        Assert.Contains("ChaosEffectBannerOverlay.Show(\"dvd\", Loc.Get(\"chaos_dvd_banner\")", source);
        Assert.Contains("_state.PushEvent(Loc.Get(\"chaos_dvd_loading\"))", source);
        Assert.DoesNotContain("\"Porn DVD\"", source);
        Assert.DoesNotContain("\"📀 now loading\"", source);
    }

    private static string Root([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
