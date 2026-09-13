using Translator.Offline;

namespace Translator.Tests.Offline;

public class TextSegmenterTests
{
    [Fact]
    public void SplitsSentencesAndKeepsSeparators()
    {
        var result = TextSegmenter.Split("Hello there. How are you? Fine!");
        Assert.Equal("", result.Leading);
        Assert.Equal(["Hello there.", "How are you?", "Fine!"], result.Segments.Select(s => s.Text));
        Assert.Equal([" ", " ", ""], result.Segments.Select(s => s.Separator));
    }

    [Fact]
    public void KeepsNewlinesAndBlankLines()
    {
        var result = TextSegmenter.Split("  First line\r\nSecond line\n\n  Third.  ");
        Assert.Equal("  ", result.Leading);
        Assert.Equal(["First line", "Second line", "Third."], result.Segments.Select(s => s.Text));
        Assert.Equal(["\r\n", "\n\n  ", "  "], result.Segments.Select(s => s.Separator));
    }

    [Theory]
    [InlineData("Dr. Smith arrived. He sat down.", new[] { "Dr. Smith arrived.", "He sat down." })]
    [InlineData("J. R. R. Tolkien wrote books.", new[] { "J. R. R. Tolkien wrote books." })]
    [InlineData("It costs 3.14 dollars, e.g. cheap. Yes.", new[] { "It costs 3.14 dollars, e.g. cheap.", "Yes." })]
    [InlineData("Wait... what? No.", new[] { "Wait... what?", "No." })]
    [InlineData("He said \"Stop!\" Then he left.", new[] { "He said \"Stop!\"", "Then he left." })]
    [InlineData("Это т.е. пример. Второе предложение.", new[] { "Это т.е. пример.", "Второе предложение." })]
    [InlineData("今日は晴れです。明日は雨です。", new[] { "今日は晴れです。", "明日は雨です。" })]
    public void HandlesAbbreviationsNumbersAndScripts(string text, string[] expected) =>
        Assert.Equal(expected, TextSegmenter.Split(text).Segments.Select(s => s.Text));

    [Fact]
    public void JoinFollowsTheTargetScript()
    {
        var segmented = TextSegmenter.Split("One. Two.\nThree.");
        Assert.Equal("一。二。\n三。", TextSegmenter.Join(segmented, ["一。", "二。", "三。"], "zh-CN"));
        var cjk = TextSegmenter.Split("一。二。");
        Assert.Equal("One. Two.", TextSegmenter.Join(cjk, ["One.", "Two."], "en"));
    }

    [Fact]
    public void EmptyAndWhitespaceTextHaveNoSegments()
    {
        Assert.Empty(TextSegmenter.Split("").Segments);
        var whitespace = TextSegmenter.Split(" \n ");
        Assert.Empty(whitespace.Segments);
        Assert.Equal(" \n ", whitespace.Leading);
    }

    [Fact]
    public void LongSentenceIsSplitWithinTheTokenBudget()
    {
        static int CountWords(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var text = "one two three, four five six seven, eight nine ten eleven twelve thirteen fourteen fifteen";
        var parts = LongSegmentSplitter.Split(text, CountWords, maxTokens: 5);

        Assert.All(parts, part => Assert.True(CountWords(part.Text) <= 5, part.Text));
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text + p.Separator)));
        Assert.Equal("", parts[^1].Separator);
    }

    [Fact]
    public void TextWithoutSpacesIsSplitByCharacters()
    {
        var text = new string('字', 100);
        var parts = LongSegmentSplitter.Split(text, s => s.Length, maxTokens: 30);
        Assert.All(parts, part => Assert.True(part.Text.Length <= 30));
        Assert.Equal(text, string.Concat(parts.Select(p => p.Text)));
    }

    [Fact]
    public void ShortSentenceIsNotSplit() =>
        Assert.Equal("Short one.", Assert.Single(LongSegmentSplitter.Split("Short one.", s => s.Length, 100)).Text);
}
