using System.Net;
using System.Net.Http;
using Translator.Core;

namespace Translator.Tests.Core;

public class GoogleTranslateEngineTests
{
    [Fact]
    public void ParseResponse_SingleSegment()
    {
        var result = GoogleTranslateEngine.ParseResponse("""[[["Hello","Привет",null,null,1]],null,"ru"]""");
        Assert.Equal("Hello", result.Text);
        Assert.Equal("ru", result.DetectedSource);
    }

    [Fact]
    public void ParseResponse_MultipleSegmentsConcatenate()
    {
        var result = GoogleTranslateEngine.ParseResponse(
            """[[["Hello ","Привет ",null,null,1],["world","мир",null,null,1]],null,"ru"]""");
        Assert.Equal("Hello world", result.Text);
    }

    [Fact]
    public void ParseResponse_NoDetectedSource()
    {
        var result = GoogleTranslateEngine.ParseResponse("""[[["Hola","Hello",null,null,1]]]""");
        Assert.Equal("Hola", result.Text);
        Assert.Null(result.DetectedSource);
    }

    [Fact]
    public void ParseResponse_DetectedSourceNotAString()
    {
        var result = GoogleTranslateEngine.ParseResponse("""[[["Hola","Hello",null,null,1]],null,null]""");
        Assert.Null(result.DetectedSource);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[[]]")]
    public void ParseResponse_MalformedThrows(string json)
    {
        Assert.Throws<GoogleTranslateException>(() => GoogleTranslateEngine.ParseResponse(json));
    }

    [Fact]
    public async Task TranslateAsync_NonOkStatus_ThrowsWithHttpMessage()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var engine = new GoogleTranslateEngine(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<GoogleTranslateException>(
            () => engine.TranslateAsync("hi", "en", "ru"));
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task TranslateAsync_Success_ReturnsParsedTranslation()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[[["Привет","Hello",null,null,1]],null,"en"]"""),
        });
        var engine = new GoogleTranslateEngine(new HttpClient(handler));

        var result = await engine.TranslateAsync("Hello", "en", "ru");
        Assert.Equal("Привет", result.Text);
        Assert.Equal("en", result.DetectedSource);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }
}
