using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Translator.Platform;

namespace Translator.UI.Shell;

/// <summary>
/// Shows a themed WPF <see cref="ContextMenu"/> at the cursor for the tray icon. The menu is owned by an
/// invisible, activated host window: without an active window of ours, clicks elsewhere wouldn't close it.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly Window _host;
    private ContextMenu? _menu;

    public TrayMenu()
    {
        _host = new Window
        {
            Title = "TranslatorTrayMenuHost",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Width = 1,
            Height = 1,
            Left = -32000,
            Top = -32000,
            Background = Brushes.Transparent,
        };
        _host.SourceInitialized += (_, _) => WindowPlacement.SetToolWindow(new WindowInteropHelper(_host).Handle);
        _host.Deactivated += (_, _) => Close();
    }

    public bool IsOpen => _menu?.IsOpen == true;

    public void Show(ContextMenu menu)
    {
        Close();
        _host.Show();
        if (!_host.Activate())
        {
            ForegroundWindow.Activate(new WindowInteropHelper(_host).Handle);
        }
        menu.Placement = PlacementMode.MousePoint;
        menu.PlacementTarget = _host;
        menu.Closed += OnMenuClosed;
        _menu = menu;
        menu.IsOpen = true;
        menu.Focus();
    }

    public void Close()
    {
        if (_menu is { IsOpen: true } menu)
        {
            menu.IsOpen = false;
        }
    }

    public void Dispose()
    {
        Close();
        _host.Close();
    }

    private void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        menu.Closed -= OnMenuClosed;
        if (ReferenceEquals(_menu, menu))
        {
            _menu = null;
        }
        _host.Hide();
    }
}
