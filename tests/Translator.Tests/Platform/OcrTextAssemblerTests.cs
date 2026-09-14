using Translator.Platform.Ocr;

namespace Translator.Tests.Platform;

public class OcrTextAssemblerTests
{
    /// <summary>A line of space-separated words, each <paramref name="charWidth"/> per character with a one-character gap.</summary>
    private static OcrLineBox Line(string text, double left, double top, double charWidth = 8, double height = 16)
    {
        var words = new List<OcrWordBox>();
        var x = left;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add(new OcrWordBox(word, x, top, word.Length * charWidth, height));
            x += (word.Length + 1) * charWidth;
        }
        return new OcrLineBox(words);
    }

    /// <summary>One OCR "word" per character, as Windows OCR reports Chinese and Japanese.</summary>
    private static OcrLineBox CharLine(string text, double left, double top, double size = 16) =>
        new(text.Select((c, i) => new OcrWordBox(c.ToString(), left + i * size, top, size, size)).ToList());

    private static OcrLineBox Boxes(params (string Text, double X, double Y, double W, double H)[] words) =>
        new(words.Select(w => new OcrWordBox(w.Text, w.X, w.Y, w.W, w.H)).ToList());

    [Fact]
    public void Latin_hyphenation_at_the_end_of_a_line_is_merged()
    {
        var text = OcrTextAssembler.Assemble(
        [
            Line("Screen text recog-", 0, 0),
            Line("nition works offline.", 0, 22),
        ]);

        Assert.Equal("Screen text recognition works offline.", text);
    }

    [Fact]
    public void Hyphen_before_a_capital_letter_is_kept_without_a_space()
    {
        var text = OcrTextAssembler.Assemble([Line("Jean-", 0, 0, height: 16), Line("Paul Sartre", 0, 22)]);

        Assert.Equal("Jean-Paul Sartre", text);
    }

    [Fact]
    public void Real_english_ocr_boxes_become_one_paragraph()
    {
        // Windows OCR (en-US) word boxes of a 16 px Segoe UI paragraph.
        var text = OcrTextAssembler.Assemble(
        [
            Boxes(("The", 6, 11, 25, 12), ("quick", 35, 11, 37, 16), ("brown", 77, 11, 43, 12), ("fox", 125, 11, 21, 12),
                ("jumps", 148, 11, 44, 16), ("over", 197, 15, 31, 8), ("the", 232, 11, 22, 12), ("lazy", 259, 11, 26, 16)),
            Boxes(("dog", 6, 32, 26, 16), ("near", 38, 36, 30, 8), ("the", 72, 32, 22, 12), ("riverbank.", 99, 32, 68, 12),
                ("Screen", 172, 33, 45, 11), ("text", 222, 34, 25, 10), ("recog-", 252, 36, 44, 12)),
            Boxes(("nition", 7, 53, 38, 12), ("works", 50, 53, 41, 12), ("offline", 96, 53, 44, 12), ("on", 144, 57, 17, 8),
                ("this", 166, 53, 24, 12), ("computer.", 195, 55, 70, 14)),
        ]);

        Assert.Equal("The quick brown fox jumps over the lazy dog near the riverbank. Screen text recognition works offline on this computer.", text);
    }

    [Fact]
    public void Real_cyrillic_ocr_boxes_merge_hyphenation_and_split_paragraphs_on_a_large_gap()
    {
        // Windows OCR (ru) word boxes: three hyphenated lines, an empty line, then a second paragraph.
        var text = OcrTextAssembler.Assemble(
        [
            Boxes(("Переводчик", 7, 12, 87, 15), ("распознаёт", 99, 12, 80, 15), ("текст", 183, 15, 37, 8), ("на", 225, 15, 15, 8), ("экра-", 245, 15, 38, 12)),
            Boxes(("не", 7, 36, 16, 8), ("и", 28, 36, 7, 8), ("показывает", 41, 36, 82, 8), ("перевод", 128, 36, 59, 12),
                ("рядом", 192, 36, 44, 12), ("с", 241, 36, 7, 8), ("выде-", 253, 36, 41, 10)),
            Boxes(("ленной", 6, 54, 51, 11), ("областью.", 62, 53, 72, 12), ("Всё", 139, 54, 23, 11), ("работает", 167, 53, 64, 16), ("офлайн.", 235, 54, 57, 15)),
            Boxes(("Второй", 7, 96, 50, 15), ("абзац", 62, 95, 42, 14), ("начинается", 109, 99, 80, 8), ("после", 195, 99, 40, 8),
                ("пустой", 240, 96, 47, 15), ("строки.", 292, 99, 52, 12)),
        ]);

        Assert.Equal(
            "Переводчик распознаёт текст на экране и показывает перевод рядом с выделенной областью. Всё работает офлайн.\n" +
            "Второй абзац начинается после пустой строки.",
            text);
    }

    [Fact]
    public void Japanese_lines_are_joined_without_spaces()
    {
        var text = OcrTextAssembler.Assemble([CharLine("これは日本語の", 0, 0), CharLine("文章です。", 0, 24)]);

        Assert.Equal("これは日本語の文章です。", text);
    }

    [Fact]
    public void Chinese_lines_are_joined_without_spaces()
    {
        var text = OcrTextAssembler.Assemble([CharLine("这是一段用于测试的", 0, 0), CharLine("中文文字。", 0, 24)]);

        Assert.Equal("这是一段用于测试的中文文字。", text);
    }

    [Fact]
    public void Korean_keeps_spaces_between_words_and_lines()
    {
        var text = OcrTextAssembler.Assemble([Line("한국어 텍스트를", 0, 0, charWidth: 16), Line("인식합니다.", 0, 24, charWidth: 16)]);

        Assert.Equal("한국어 텍스트를 인식합니다.", text);
    }

    [Fact]
    public void A_line_that_ended_early_starts_a_new_paragraph()
    {
        var text = OcrTextAssembler.Assemble(
        [
            Line("First paragraph ends here.", 0, 0),
            Line("Second paragraph starts on a new line and is long.", 0, 22),
            Line("It continues on this line.", 0, 44),
        ]);

        Assert.Equal("First paragraph ends here.\nSecond paragraph starts on a new line and is long. It continues on this line.", text);
    }

    [Fact]
    public void A_large_vertical_gap_starts_a_new_paragraph_even_between_full_lines()
    {
        var text = OcrTextAssembler.Assemble(
        [
            Line("Lorem ipsum dolor sit amet, consectetur", 0, 0),
            Line("adipiscing elit, sed do eiusmod tempor", 0, 22),
            Line("Ut enim ad minim veniam, quis nostrud a", 0, 70),
            Line("exercitation ullamco laboris nisi ut al", 0, 92),
        ]);

        Assert.Equal("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor\nUt enim ad minim veniam, quis nostrud a exercitation ullamco laboris nisi ut al", text);
    }

    [Fact]
    public void Double_spaced_text_is_not_split_into_one_paragraph_per_line()
    {
        var text = OcrTextAssembler.Assemble(
        [
            Line("Lorem ipsum dolor sit amet, consectetur", 0, 0),
            Line("adipiscing elit, sed do eiusmod tempora", 0, 32),
            Line("incididunt ut labore et dolore magna al", 0, 64),
        ]);

        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void A_first_line_indent_starts_a_new_paragraph()
    {
        var text = OcrTextAssembler.Assemble(
        [
            Line("Lorem ipsum dolor sit amet, consectetur", 0, 0),
            Line("Indented start of the next paragraph", 40, 22),
        ]);

        Assert.Equal("Lorem ipsum dolor sit amet, consectetur\nIndented start of the next paragraph", text);
    }

    [Fact]
    public void Bulleted_lines_stay_separate()
    {
        var text = OcrTextAssembler.Assemble([Line("• First item of a long list", 0, 0), Line("• Second", 0, 22)]);

        Assert.Equal("• First item of a long list\n• Second", text);
    }

    [Fact]
    public void Empty_input_and_blank_words_give_empty_text()
    {
        Assert.Equal(string.Empty, OcrTextAssembler.Assemble([]));
        Assert.Equal(string.Empty, OcrTextAssembler.Assemble([new OcrLineBox([new OcrWordBox(" ", 0, 0, 5, 5)])]));
        Assert.Null(OcrTextAssembler.MedianLineHeight([]));
    }

    [Fact]
    public void Median_line_height_uses_the_line_boxes()
    {
        Assert.Equal(16, OcrTextAssembler.MedianLineHeight([Line("a b", 0, 0, height: 12), Line("c d", 0, 20, height: 16), Line("e", 0, 40, height: 30)]));
    }
}
