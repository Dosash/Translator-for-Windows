using Translator.Platform;

namespace Translator.Tests.Platform;

public class SingleInstanceTests
{
    private static string UniqueAppId([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        $"TranslatorTest-{name}-{Guid.NewGuid():N}";

    [Fact]
    public void TryAcquire_succeeds_when_nobody_else_holds_it()
    {
        var appId = UniqueAppId();
        using var instance = SingleInstance.TryAcquire(appId);
        Assert.NotNull(instance);
    }

    [Fact]
    public void Second_TryAcquire_for_the_same_id_returns_null()
    {
        var appId = UniqueAppId();
        using var first = SingleInstance.TryAcquire(appId);
        Assert.NotNull(first);

        var second = SingleInstance.TryAcquire(appId);
        Assert.Null(second);
    }

    [Fact]
    public async Task SendToPrimary_delivers_arguments_to_the_listening_instance()
    {
        var appId = UniqueAppId();
        using var primary = SingleInstance.TryAcquire(appId);
        Assert.NotNull(primary);

        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary!.ArgumentsReceived += (_, args) => received.TrySetResult(args);
        primary.StartListening();

        var expected = new[] { "--translate", "hello world" };
        var sent = SingleInstance.SendToPrimary(appId, expected, TimeSpan.FromSeconds(5));
        Assert.True(sent);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(received.Task, completed);
        Assert.Equal(expected, await received.Task);
    }

    [Fact]
    public void SendToPrimary_returns_false_when_nobody_is_listening()
    {
        var appId = UniqueAppId();
        var sent = SingleInstance.SendToPrimary(appId, ["--translate", "x"], TimeSpan.FromMilliseconds(200));
        Assert.False(sent);
    }
}
