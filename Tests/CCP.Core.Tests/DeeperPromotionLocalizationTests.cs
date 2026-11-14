using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

[Collection(GlyphFallbackLanguageCollection.Name)]
public sealed class DeeperPromotionLocalizationTests
{
    [Theory]
    [InlineData("en", "Saved to library as demo.ccpenh.json")]
    [InlineData("it", "Salvato nella libreria come demo.ccpenh.json")]
    [InlineData("zh-CN", "已保存到库，名称为 demo.ccpenh.json")]
    [InlineData("de", "Als demo.ccpenh.json in der Bibliothek gespeichert")]
    [InlineData("es", "Guardado en la biblioteca como demo.ccpenh.json")]
    [InlineData("fr", "Enregistré dans la bibliothèque sous le nom demo.ccpenh.json")]
    [InlineData("ja", "demo.ccpenh.json としてライブラリに保存しました")]
    [InlineData("ko", "demo.ccpenh.json 이름으로 라이브러리에 저장함")]
    [InlineData("pt-BR", "Salvo na biblioteca como demo.ccpenh.json")]
    [InlineData("ru", "Сохранено в библиотеке как demo.ccpenh.json")]
    public void PromotionHintUsesTheSelectedLanguage(string language, string expected)
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            Assert.Equal(expected, Loc.GetF("deeper_player_promoted_to_library_fmt", "demo.ccpenh.json"));
        }
        finally { loc.SetLanguage(previous); }
    }
}
