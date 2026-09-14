using Translator.Platform;

namespace Translator.Tests.Platform;

public class TrayIconMessageTests
{
    [Fact]
    public void Version4RightClickOpensTheMenuOnlyOnContextMenu()
    {
        // One right click: the shell sends WM_RBUTTONUP and then WM_CONTEXTMENU.
        Assert.Equal(TrayMouseAction.None, TrayIcon.ClassifyMouseMessage(NativeMethods.WM_RBUTTONUP, version4: true));
        Assert.Equal(TrayMouseAction.Context, TrayIcon.ClassifyMouseMessage(NativeMethods.WM_CONTEXTMENU, version4: true));
    }

    [Fact]
    public void LegacyShellRightClickOpensTheMenuOnButtonUp() =>
        Assert.Equal(TrayMouseAction.Context, TrayIcon.ClassifyMouseMessage(NativeMethods.WM_RBUTTONUP, version4: false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeftButtonUpIsPrimary(bool version4) =>
        Assert.Equal(TrayMouseAction.Primary, TrayIcon.ClassifyMouseMessage(NativeMethods.WM_LBUTTONUP, version4));

    [Fact]
    public void OtherMouseMessagesAreIgnored() =>
        Assert.Equal(TrayMouseAction.None, TrayIcon.ClassifyMouseMessage(0x0200 /* WM_MOUSEMOVE */, version4: true));
}
