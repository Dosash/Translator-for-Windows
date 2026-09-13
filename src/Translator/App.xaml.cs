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
    private SingleInstance? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterExceptionLogging();

        var command = CommandLine.Parse(e.Args);
        switch (command.Kind)
        {
            case CliCommandKind.TestTranslate:
                ExitWith(RunTestTranslate(command.Text!));
                return;
            case CliCommandKind.TestOffline:
                ExitWith(RunTestOffline(command));
                return;
            case CliCommandKind.Invalid:
                ConsoleAttach.AttachToParent();
                Console.Error.WriteLine(command.Error);
                ExitWith(2);
                return;
        }

        _singleInstance = SingleInstance.TryAcquire(AppInfo.AppId);
        if (_singleInstance is null)
        {
            // Let the running instance take the foreground when it shows the panel.
            UiNativeMethods.AllowSetForegroundWindow(UiNativeMethods.ASFW_ANY);
            var delivered = SingleInstance.SendToPrimary(AppInfo.AppId, e.Args, TimeSpan.FromSeconds(3));
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
