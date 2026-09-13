using System.Windows;

namespace Translator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Placeholder: the app shell (tray, hotkeys, windows) is wired by AppController.
        Shutdown();
    }
}
