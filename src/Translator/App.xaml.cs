using System.Windows;
using System.Windows.Threading;
using Translator.Core;
using Translator.Offline;
using Translator.Platform;
using Translator.UI;
using Translator.UI.Interop;
using Translator.UI.Shell;

namespace Translator;

public partial class App : Application
{
    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProcessExitGrace = TimeSpan.FromSeconds(5);

    private SingleInstance? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterExceptionLogging();

        var instanceId = AppPaths.InstanceId(AppInfo.AppId, AppPaths.DataDirectoryOverride);
        var command = CommandLine.Parse(e.Args);
        switch (command.Kind)
        {
            case CliCommandKind.TestTranslate:
                ExitWith(RunTestTranslate(command.Text!));
                return;
            case CliCommandKind.TestOffline:
                ExitWith(RunTestOffline(command));
                return;
            case CliCommandKind.Quit:
                ExitWith(RunQuit(instanceId));
                return;
            case CliCommandKind.Invalid:
                ConsoleAttach.AttachToParent();
                Console.Error.WriteLine(command.Error);
                ExitWith(2);
                return;
        }

        _singleInstance = SingleInstance.TryAcquire(instanceId);
        if (_singleInstance is null)
        {
            // Let the running instance take the foreground when it shows the panel.
            UiNativeMethods.AllowSetForegroundWindow(UiNativeMethods.ASFW_ANY);
            var delivered = SingleInstance.SendToPrimary(instanceId, e.Args, TimeSpan.FromSeconds(3));
            DebugLog.Write($"App: another instance is running, arguments delivered={delivered}");
            ExitWith(delivered ? 0 : 1);
            return;
        }

        DebugLog.Write($"App: starting {AppInfo.Version} ({command.Kind})");
        _controller = new AppController(this);
        _singleInstance.ArgumentsReceived += (_, args) => Dispatcher.BeginInvoke(() => _controller?.HandleArguments(args));
        _singleInstance.StartListening();
        _controller.Start(command);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void ExitWith(int exitCode)
    {
        Environment.ExitCode = exitCode;
        Shutdown(exitCode);
    }

    /// <summary>
    /// <c>--quit</c>: asks the running instance to shut down and waits until it's gone, so the installer can
    /// replace files right after. Never starts the app; exit code 0 also when nothing was running.
    /// </summary>
    private static int RunQuit(string instanceId)
    {
        if (!SingleInstance.IsRunning(instanceId))
        {
            return 0;
        }
        if (!SingleInstance.SendToPrimary(instanceId, [CommandLine.QuitFlag], TimeSpan.FromSeconds(3)))
        {
            return 1;
        }
        if (!SingleInstance.WaitUntilNotRunning(instanceId, QuitTimeout))
        {
            DebugLog.Write("App: --quit timed out waiting for the running instance");
            return 1;
        }
        WaitForProcessesFromSameExecutable(ProcessExitGrace);
        return 0;
    }

    /// <summary>The instance releases its mutex just before the process ends; files stay locked until it has.</summary>
    private static void WaitForProcessesFromSameExecutable(TimeSpan timeout)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var deadline = DateTime.UtcNow + timeout;
        var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(path)))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.SessionId != sessionId
                        || !string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        process.WaitForExit(remaining);
                    }
                }
                catch (Exception)
                {
                    // Exited or inaccessible meanwhile.
                }
            }
        }
    }

    private static int RunTestTranslate(string text)
    {
        ConsoleAttach.AttachToParent();
        try
        {
            var (source, target) = CommandLine.DetectDirection(text);
            // Off the UI thread: the engine resumes on the captured context, which a blocking wait would deadlock.
            var result = Task.Run(() => new GoogleTranslateEngine().TranslateAsync(text, source, target)).GetAwaiter().GetResult();
            Console.Out.WriteLine(result.Text);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int RunTestOffline(CliCommand command)
    {
        ConsoleAttach.AttachToParent();
        try
        {
            using var manager = new OfflineModelManager(AppPaths.ModelsDirectory);
            var result = Task.Run(() => manager.TranslateAsync(command.Text!, command.Source, command.Target!)).GetAwaiter().GetResult();
            Console.Out.WriteLine(result.Text);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private void RegisterExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"Unhandled UI exception: {Describe(args.Exception)}");
            // Keep the tray app alive; only give up on conditions the process can't recover from.
            args.Handled = args.Exception is not (OutOfMemoryException or InsufficientExecutionStackException);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DebugLog.Write($"Unhandled exception (terminating={args.IsTerminating}): {Describe(args.ExceptionObject as Exception)}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DebugLog.Write($"Unobserved task exception: {Describe(args.Exception)}");
            args.SetObserved();
        };
    }

    /// <summary>Type and stack only: exception messages can quote translated text, which must never be logged.</summary>
    private static string Describe(Exception? exception)
    {
        if (exception is null)
        {
            return "unknown";
        }
        var inner = exception.InnerException is { } innerException ? $" <- {innerException.GetType().FullName}" : string.Empty;
        return $"{exception.GetType().FullName}{inner}{Environment.NewLine}{exception.StackTrace}";
    }
}
