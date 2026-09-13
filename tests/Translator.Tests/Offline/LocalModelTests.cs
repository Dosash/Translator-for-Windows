using System.IO;
using Translator.Offline;

namespace Translator.Tests.Offline;

/// <summary>
/// Runs against real downloaded models (OfflineCli download ru|de|ko|tr) and is skipped when they are absent.
/// Reference ids come from Hugging Face transformers via tools/OfflineCli/scripts/marian_parity.py.
/// </summary>
public class LocalModelTests
{
    [RequiresModelsFact("opus-mt-ru-en", "opus-mt-en-ru")]
    public void RussianTokenizersMatchTransformers() => AssertParity("opus-mt-ru-en", "opus-mt-en-ru");

    [RequiresModelsFact("opus-mt-de-en", "opus-mt-en-de")]
    public void GermanTokenizersMatchTransformers() => AssertParity("opus-mt-de-en", "opus-mt-en-de");

    [RequiresModelsFact("opus-mt-ko-en", "opus-mt-en-mul")]
    public void MultilingualTokenizersMatchTransformers() => AssertParity("opus-mt-ko-en", "opus-mt-en-mul");

    [RequiresModelsFact("opus-mt-ru-en", "opus-mt-en-ru")]
    public async Task TranslatesBetweenEnglishAndRussian()
    {
        using var manager = new OfflineModelManager(LocalModels.Directory);

        var russian = await manager.TranslateAsync("The weather is beautiful today, so we decided to go for a walk.", "en", "ru");
        Assert.Contains("погода", russian.Text, StringComparison.OrdinalIgnoreCase);

        var english = await manager.TranslateAsync("Привет! Как дела?", null, "en");
        Assert.Equal("ru", english.SourceCode);
        Assert.Contains("How are you", english.Text, StringComparison.OrdinalIgnoreCase);

        var paragraphs = await manager.TranslateAsync("Good morning.\n\nThank you very much!", "en", "ru");
        var lines = paragraphs.Text.Split("\n\n");
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.True(HasCyrillic(line), line));
    }

    [RequiresModelsFact("opus-mt-de-en", "opus-mt-en-ru")]
    public async Task PivotsThroughEnglish()
    {
        using var manager = new OfflineModelManager(LocalModels.Directory);
        var result = await manager.TranslateAsync("Ich habe heute keine Zeit.", "de", "ru");
        Assert.True(HasCyrillic(result.Text), result.Text);
    }

    [RequiresModelsFact("opus-mt-tc-big-en-ko")]
    public async Task TranslatesEnglishToKorean()
    {
        using var manager = new OfflineModelManager(LocalModels.Directory);
        if (!manager.IsPairReady("en", "ko"))
        {
            return;
        }

        var result = await manager.TranslateAsync("2, 4, 6 etc. are even numbers.", "en", "ko");
        Assert.Contains("짝수", result.Text);
    }

    [RequiresModelsFact("opus-mt-en-ru")]
    public async Task CancellationIsHonored()
    {
        using var manager = new OfflineModelManager(LocalModels.Directory);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.TranslateAsync("Hello there.", "en", "ru", cancelled.Token));
    }

    private static void AssertParity(params string[] modelIds)
    {
        var cases = MarianParityData.Cases.Where(c => modelIds.Contains(c.Model)).ToList();
        Assert.NotEmpty(cases);
        foreach (var group in cases.GroupBy(c => c.Model))
        {
            var tokenizer = MarianTokenizer.Load(Path.Combine(LocalModels.Directory, group.Key));
            foreach (var (model, text, targetToken, ids) in group)
            {
                Assert.True(ids.SequenceEqual(tokenizer.Encode(text, targetToken)), $"{model}: {text}");
            }
        }
    }

    private static bool HasCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');
}
