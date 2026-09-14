using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Translator.Core;

namespace Translator.Platform;

/// <summary>
/// One running instance per user: a named mutex claims ownership, a named pipe forwards
/// command-line arguments from later launches (e.g. <c>--translate "text"</c>) to the first one.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _listenCts;
    private bool _disposed;

    public event EventHandler<string[]>? ArgumentsReceived;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Claims ownership for <paramref name="appId"/>, or returns null if another instance already holds it.</summary>
    public static SingleInstance? TryAcquire(string appId)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName(appId), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, PipeName(appId));
    }

    /// <summary>True while an instance for <paramref name="appId"/> exists; the mutex name disappears once its owner has closed it.</summary>
    public static bool IsRunning(string appId)
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName(appId), out var mutex))
            {
                return false;
            }
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Exists, but owned by an instance with different rights (e.g. elevated).
            return true;
        }
    }

    /// <summary>Polls until the instance for <paramref name="appId"/> has shut down. Returns false on timeout.</summary>
    public static bool WaitUntilNotRunning(string appId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsRunning(appId))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }
            Thread.Sleep(50);
        }
        return true;
    }

    /// <summary>Forwards <paramref name="args"/> to the running instance's pipe. Returns false if nobody is listening.</summary>
    public static bool SendToPrimary(string appId, string[] args, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(appId), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)Math.Max(0, timeout.TotalMilliseconds));

            var json = JsonSerializer.SerializeToUtf8Bytes(args);
            Span<byte> lengthPrefix = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, json.Length);
            client.Write(lengthPrefix);
            client.Write(json);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"SingleInstance.SendToPrimary failed: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Starts accepting connections on a background task; raises <see cref="ArgumentsReceived"/> for each one.</summary>
    public void StartListening()
    {
        _listenCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoopAsync(_listenCts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"SingleInstance: couldn't create pipe server: {ex.GetType().Name}");
                return;
            }
            using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    var lengthBuffer = new byte[4];
                    if (!await ReadExactAsync(server, lengthBuffer, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
                    if (length < 0 || length > MaxMessageBytes)
                    {
                        continue;
                    }
                    var payload = new byte[length];
                    if (!await ReadExactAsync(server, payload, ct).ConfigureAwait(false))
                    {
                        continue;
                    }
                    var args = JsonSerializer.Deserialize<string[]>(payload) ?? [];
                    ArgumentsReceived?.Invoke(this, args);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"SingleInstance: pipe read failed: {ex.GetType().Name}");
                }
            }
        }
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }
        return true;
    }

    private static string MutexName(string appId) => $@"Local\{appId}-{UserHash()}";

    private static string PipeName(string appId) => $"{appId}-{UserHash()}-pipe";

    private static string UserHash()
    {
        string sid;
        try
        {
            sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch
        {
            sid = Environment.UserName;
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sid));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _listenCts?.Cancel();
        _listenCts?.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned (shouldn't happen — we always hold it from TryAcquire).
        }
        _mutex.Dispose();
    }
}
