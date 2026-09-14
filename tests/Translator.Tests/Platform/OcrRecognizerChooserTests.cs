using Translator.Platform.Ocr;

namespace Translator.Tests.Platform;

public class OcrRecognizerChooserTests
{
    private static readonly OcrRecognizerInfo English = new("en-US", OcrScript.Latin);
    private static readonly OcrRecognizerInfo German = new("de-DE", OcrScript.Latin);
    private static readonly OcrRecognizerInfo Russian = new("ru", OcrScript.Cyrillic);
    private static readonly OcrRecognizerInfo Japanese = new("ja", OcrScript.Japanese);
    private static readonly OcrRecognizerInfo ChineseSimplified = new("zh-Hans-CN", OcrScript.Chinese);
    private static readonly OcrRecognizerInfo ChineseTraditional = new("zh-Hant-TW", OcrScript.Chinese);
    private static readonly OcrRecognizerInfo Korean = new("ko", OcrScript.Korean);
    private static readonly OcrRecognizerInfo Arabic = new("ar-SA", OcrScript.Other);

    // Real output of the Windows en-US and ru recognizers for the same rendered paragraphs.
    private const string RussianByRussian = "Переводчик распознаёт текст на экране и показывает перевод рядом с выделенной областью. Всё работает офлайн.";
    private const string RussianByEnglish = "nepe80AuVIK pacn03HaéT TeKCT Ha 3Kpa-He VI noKa3blaaeT nepeaoA PRAOM C ablAe-06nacTbi-o. Bcé pa60TaeT";
    private const string EnglishByEnglish = "The quick brown fox jumps over the lazy dog near the riverbank. Screen text recognition works offline on this computer.";
    private const string EnglishByRussian = "The quick brown foxjumps over the Iazy dog пеат- the riverbankv Screen text recognition works offine оп this computerv";

    [Theory]
    [InlineData("Latn", OcrScript.Latin)]
    [InlineData("Cyrl", OcrScript.Cyrillic)]
    [InlineData("Jpan", OcrScript.Japanese)]
    [InlineData("Hans", OcrScript.Chinese)]
    [InlineData("Hant", OcrScript.Chinese)]
    [InlineData("Kore", OcrScript.Korean)]
    [InlineData("Arab", OcrScript.Other)]
    [InlineData(null, OcrScript.Other)]
    public void Script_codes_map_to_scripts(string? code, OcrScript script)
    {
        Assert.Equal(script, OcrRecognizerChooser.ScriptFromCode(code));
    }

    [Fact]
    public void Explicit_source_with_its_recognizer_installed_uses_only_that_one()
    {
        var plan = OcrRecognizerChooser.Plan("ru", [English, Russian], "en-US");

        Assert.Equal(["ru"], plan.Tags);
        Assert.Null(plan.MissingLanguage);
    }

    [Theory]
    [InlineData("de", "en-US")]
    [InlineData("tr", "en-US")]
    [InlineData("uk", "ru")]
    public void Explicit_source_without_its_recognizer_uses_one_for_the_same_script(string source, string expected)
    {
        var plan = OcrRecognizerChooser.Plan(source, [English, Russian], "en-US");

        Assert.Equal([expected], plan.Tags);
    }

    [Fact]
    public void Explicit_simplified_chinese_prefers_the_simplified_recognizer()
    {
        var plan = OcrRecognizerChooser.Plan("zh-CN", [ChineseTraditional, ChineseSimplified, English], null);

        Assert.Equal(["zh-Hans-CN"], plan.Tags);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("zh-CN")]
    public void Explicit_source_with_no_recognizer_for_its_script_reports_the_missing_language(string source)
    {
        var plan = OcrRecognizerChooser.Plan(source, [English, Russian], "en-US");

        Assert.Empty(plan.Tags);
        Assert.Equal(source, plan.MissingLanguage);
    }

    [Fact]
    public void Auto_runs_one_recognizer_per_script_with_the_user_profile_first()
    {
        Assert.Equal(["en-US", "ru"], OcrRecognizerChooser.Plan("auto", [English, Russian], "en-US").Tags);
        Assert.Equal(["ru", "en-US"], OcrRecognizerChooser.Plan("auto", [English, Russian], "ru").Tags);
    }

    [Fact]
    public void Auto_prefers_a_latin_user_profile_recognizer_over_english()
    {
        var plan = OcrRecognizerChooser.Plan("auto", [English, German, Russian], "de-DE");

        Assert.Equal(["de-DE", "ru"], plan.Tags);
    }

    [Fact]
    public void Auto_without_a_profile_prefers_english_for_latin_and_adds_each_cjk_recognizer()
    {
        var plan = OcrRecognizerChooser.Plan("auto", [German, Korean, ChineseSimplified, Russian, Japanese, English], null);

        Assert.Equal(["en-US", "ru", "ja", "zh-Hans-CN", "ko"], plan.Tags);
    }

    [Fact]
    public void Auto_with_no_recognizer_for_any_app_language_is_empty_without_a_missing_language()
    {
        var plan = OcrRecognizerChooser.Plan("auto", [Arabic], "ar-SA");

        Assert.Empty(plan.Tags);
        Assert.Null(plan.MissingLanguage);
    }

    [Fact]
    public void Russian_text_picks_the_russian_recognizer_over_latin_lookalikes()
    {
        var best = OcrRecognizerChooser.PickBest(
        [
            new OcrCandidate("en-US", OcrScript.Latin, RussianByEnglish, 3),
            new OcrCandidate("ru", OcrScript.Cyrillic, RussianByRussian, 3),
        ]);

        Assert.Equal("ru", best?.Tag);
    }

    [Fact]
    public void English_text_picks_the_english_recognizer_over_the_russian_one()
    {
        var best = OcrRecognizerChooser.PickBest(
        [
            new OcrCandidate("ru", OcrScript.Cyrillic, EnglishByRussian, 3),
            new OcrCandidate("en-US", OcrScript.Latin, EnglishByEnglish, 3),
        ]);

        Assert.Equal("en-US", best?.Tag);
    }

    [Fact]
    public void Russian_sentence_with_english_words_still_picks_the_russian_recognizer()
    {
        var best = OcrRecognizerChooser.PickBest(
        [
            new OcrCandidate("en-US", OcrScript.Latin, "Haxt•.wre Save, UT06b1 COXpaHhTb report.docx", 1),
            new OcrCandidate("ru", OcrScript.Cyrillic, "Нажмите кнопку Save, чтобы сохранить файл report.docx", 1),
        ]);

        Assert.Equal("ru", best?.Tag);
    }

    [Fact]
    public void Japanese_text_picks_the_japanese_recognizer()
    {
        var best = OcrRecognizerChooser.PickBest(
        [
            new OcrCandidate("en-US", OcrScript.Latin, "El*a)fi-t Ä#tä", 1),
            new OcrCandidate("ja", OcrScript.Japanese, "日本語のテキストを認識します。", 1),
        ]);

        Assert.Equal("ja", best?.Tag);
    }

    [Fact]
    public void Replacement_characters_and_garbage_lower_the_score()
    {
        var clean = OcrRecognizerChooser.Score("Hello world", OcrScript.Latin);

        Assert.True(OcrRecognizerChooser.Score("Hello w�rld", OcrScript.Latin) < clean);
        Assert.True(OcrRecognizerChooser.Score("He¦lo wor¤d", OcrScript.Latin) < clean);
        Assert.True(OcrRecognizerChooser.Score("HeLlo w0rld", OcrScript.Latin) < clean);
    }

    [Fact]
    public void Ordinary_numbers_and_punctuation_are_not_penalized()
    {
        Assert.Equal(OcrRecognizerChooser.Score("Version to", OcrScript.Latin),
            OcrRecognizerChooser.Score("Version 2.0 (10%) — to", OcrScript.Latin));
    }

    [Fact]
    public void Ties_go_to_the_earlier_candidate_and_empty_results_are_skipped()
    {
        var best = OcrRecognizerChooser.PickBest(
        [
            new OcrCandidate("ru", OcrScript.Cyrillic, "   ", 0),
            new OcrCandidate("en-US", OcrScript.Latin, "same text", 1),
            new OcrCandidate("de-DE", OcrScript.Latin, "same text", 1),
        ]);

        Assert.Equal("en-US", best?.Tag);
        Assert.Null(OcrRecognizerChooser.PickBest([new OcrCandidate("en-US", OcrScript.Latin, "", 0)]));
    }
}
