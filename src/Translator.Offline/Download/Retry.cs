using System.Net;

namespace Translator.Offline;

internal static class Retry
{
    public static TimeSpan DefaultDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts,
        Func<int, TimeSpan> delay,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex, cancellationToken))
            {
                await Task.Delay(delay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static bool IsTransient(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => !cancellationToken.IsCancellationRequested, // timeouts, not user cancellation
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } status } =>
            (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        IOException => true,
        _ => false,
    };
}
