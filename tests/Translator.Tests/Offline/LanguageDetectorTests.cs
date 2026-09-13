using Translator.Offline;

namespace Translator.Tests.Offline;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("Привет, как дела? Всё хорошо.", "ru")]
    [InlineData("Съешь ещё этих мягких французских булок", "ru")]
    [InlineData("Добрий день! Як справи? Їжак і ґанок.", "uk")]
    [InlineData("안녕하세요, 만나서 반갑습니다.", "ko")]
    [InlineData("日本語のテキストです。", "ja")]
    [InlineData("東京は日本の首都です。", "ja")]
    [InlineData("这是一个中文句子。", "zh-CN")]
    [InlineData("The weather is nice today and I want to go outside.", "en")]
    [InlineData("El perro está en la casa y no quiere salir.", "es")]
    [InlineData("¿Dónde está la estación?", "es")]
    [InlineData("Der Hund ist nicht im Haus und will nicht raus.", "de")]
    [InlineData("Grüße aus der Straße", "de")]
    [InlineData("Le chat est dans la maison et il ne veut pas sortir.", "fr")]
    [InlineData("Il gatto è nella casa e non vuole uscire.", "it")]
    [InlineData("O cachorro não está em casa, ele foi para a rua.", "pt")]
    [InlineData("Bugün hava çok güzel ve dışarı çıkmak istiyorum.", "tr")]
    public void DetectsLanguage(string text, string expected) =>
        Assert.Equal(expected, OfflineModelManager.DetectLanguage(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345 67.89")]
    [InlineData("😀👍")]
    [InlineData("OK")]
    [InlineData("Toyota Corolla")]
    public void ReturnsNullWhenUnsure(string text) =>
        Assert.Null(OfflineModelManager.DetectLanguage(text));

    [Fact]
    public void DominantScriptWins() =>
        Assert.Equal("ru", OfflineModelManager.DetectLanguage("Я использую Windows каждый день"));
}
