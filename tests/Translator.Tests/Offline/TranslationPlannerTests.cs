using Translator.Offline;

namespace Translator.Tests.Offline;

public class TranslationPlannerTests
{
    [Fact]
    public void FromEnglishUsesOneModel()
    {
        var step = Assert.Single(TranslationPlanner.Plan("en", "ru"));
        Assert.Equal("opus-mt-en-ru", step.Model.Id);
        Assert.Null(step.TargetToken);
    }

    [Fact]
    public void ToEnglishUsesOneModel()
    {
        var step = Assert.Single(TranslationPlanner.Plan("de", "en"));
        Assert.Equal("opus-mt-de-en", step.Model.Id);
        Assert.Equal(("de", "en"), (step.SourceCode, step.TargetCode));
    }

    [Fact]
    public void NonEnglishPairPivotsThroughEnglish()
    {
        var steps = TranslationPlanner.Plan("de", "ru");
        Assert.Equal(["opus-mt-de-en", "opus-mt-en-ru"], steps.Select(s => s.Model.Id));
        Assert.Equal(["de", "en"], steps.Select(s => s.SourceCode));
        Assert.Equal(["en", "ru"], steps.Select(s => s.TargetCode));
    }

    [Fact]
    public void MultilingualTargetCarriesItsToken()
    {
        var steps = TranslationPlanner.Plan("ru", "tr");
        Assert.Equal(2, steps.Count);
        Assert.Null(steps[0].TargetToken);
        Assert.Equal(">>tur<<", steps[1].TargetToken);
        Assert.Equal(">>cmn_Hans<<", Assert.Single(TranslationPlanner.Plan("en", "zh-cn")).TargetToken);
    }

    [Fact]
    public void SameLanguageNeedsNoModels() => Assert.Empty(TranslationPlanner.Plan("ru", "ru"));

    [Fact]
    public void UnsupportedLanguageIsRejected() =>
        Assert.Throws<OfflineTranslationException>(() => TranslationPlanner.Plan("en", "xx"));
}
