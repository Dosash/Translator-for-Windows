using Translator.Offline;

namespace Translator.Tests.Offline;

public class OfflineCatalogTests
{
    private static readonly string[] AppLanguages = ["ru", "en", "es", "de", "fr", "it", "pt", "zh-CN", "ja", "ko", "tr", "uk"];

    [Fact]
    public void EveryAppLanguageExceptEnglishHasBothDirections()
    {
        Assert.Equal(AppLanguages.Where(code => code != "en"), OfflineModelManager.SupportedLanguages);
        foreach (var code in OfflineModelManager.SupportedLanguages)
        {
            var language = OfflineModelCatalog.FindLanguage(code);
            Assert.NotNull(language);
            var toEnglish = OfflineModelCatalog.FindModel(language.ToEnglishModelId);
            var fromEnglish = OfflineModelCatalog.FindModel(language.FromEnglishModelId);
            Assert.NotNull(toEnglish);
            Assert.NotNull(fromEnglish);
            Assert.EndsWith("-en", toEnglish.Id, StringComparison.Ordinal);
            Assert.Contains("-en-", fromEnglish.Id, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryModelIsUsedAndHasTheInferenceFiles()
    {
        Assert.Equal(OfflineModelCatalog.Models.Count, OfflineModelCatalog.Models.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var model in OfflineModelCatalog.Models)
        {
            Assert.NotEmpty(OfflineModelCatalog.LanguagesUsingModel(model.Id));
            var paths = model.Files.Select(f => f.Path).ToList();
            Assert.Equal(8, paths.Distinct().Count());
            Assert.Contains(model.EncoderPath, paths);
            Assert.Contains(model.DecoderPath, paths);
            Assert.Contains("source.spm", paths);
            Assert.Contains("vocab.json", paths);
            Assert.Contains("config.json", paths);
            Assert.True(model.EstimatedSizeBytes > 50 * 1024 * 1024);
        }
    }

    [Fact]
    public void MultilingualTargetModelsRequireATargetToken()
    {
        foreach (var language in OfflineModelCatalog.Languages)
        {
            var multilingual = language.FromEnglishModelId is "opus-mt-en-mul" or "opus-mt-en-ROMANCE" or "opus-mt-en-zh";
            Assert.Equal(multilingual, language.FromEnglishTargetToken is not null);
            if (language.FromEnglishTargetToken is { } token)
            {
                Assert.Matches("^>>[A-Za-z_]+<<$", token);
            }
        }
    }

    [Fact]
    public void BasicQualityMarksTheWeakDirections()
    {
        foreach (var language in OfflineModelCatalog.Languages)
        {
            var expected = language.FromEnglishModelId is "opus-mt-en-mul" or "opus-mt-en-jap";
            Assert.Equal(expected, OfflineModelManager.IsBasicQuality(language.Code));
        }

        Assert.False(OfflineModelManager.IsBasicQuality("en"));
        Assert.False(OfflineModelManager.IsBasicQuality("xx"));
    }

    [Fact]
    public void SharedModelsListEveryLanguageThatUsesThem()
    {
        foreach (var model in OfflineModelCatalog.Models)
        {
            var expected = OfflineModelCatalog.Languages.Where(l => l.ModelIds.Contains(model.Id)).Select(l => l.Code);
            Assert.Equal(expected, OfflineModelCatalog.LanguagesUsingModel(model.Id));
        }
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-cn", "zh-CN")]
    [InlineData("ZH", "zh-CN")]
    [InlineData(" RU ", "ru")]
    [InlineData("en", "en")]
    public void NormalizesLanguageCodes(string input, string expected) =>
        Assert.Equal(expected, OfflineModelCatalog.NormalizeCode(input));
}
