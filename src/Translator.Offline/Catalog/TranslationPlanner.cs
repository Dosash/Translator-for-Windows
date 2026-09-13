namespace Translator.Offline;

internal sealed record TranslationStep(string SourceCode, string TargetCode, OfflineModelInfo Model, string? TargetToken);

/// <summary>Plans X→Y as X→en→Y, because every model translates to or from English.</summary>
internal static class TranslationPlanner
{
    public static IReadOnlyList<TranslationStep> Plan(string sourceCode, string targetCode)
    {
        var source = OfflineModelCatalog.NormalizeCode(sourceCode);
        var target = OfflineModelCatalog.NormalizeCode(targetCode);
        if (source == target)
        {
            return [];
        }

        var steps = new List<TranslationStep>(2);
        if (source != OfflineModelCatalog.BaseLanguage)
        {
            var language = Require(source);
            steps.Add(new(source, OfflineModelCatalog.BaseLanguage, OfflineModelCatalog.GetModel(language.ToEnglishModelId), null));
        }

        if (target != OfflineModelCatalog.BaseLanguage)
        {
            var language = Require(target);
            steps.Add(new(
                OfflineModelCatalog.BaseLanguage,
                target,
                OfflineModelCatalog.GetModel(language.FromEnglishModelId),
                language.FromEnglishTargetToken));
        }

        return steps;
    }

    private static OfflineLanguageInfo Require(string code) =>
        OfflineModelCatalog.FindLanguage(code)
        ?? throw new OfflineTranslationException($"Language '{code}' is not supported offline.");
}
