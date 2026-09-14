using Translator.Platform;

namespace Translator.Tests.Platform;

/// <summary>
/// Verifies the chord picked per <see cref="CopyPasteChord"/> without touching the real keyboard —
/// <see cref="InputSimulator.RecordedChords"/> is a test seam that captures (modifier, key) instead
/// of calling SendInput.
/// </summary>
public class InputSimulatorChordTests
{
    private static (int Modifier, int Vk)? Capture(Action action)
    {
        var recorded = new List<(int Modifier, int Vk)?>();
        var previous = InputSimulator.RecordedChords;
        InputSimulator.RecordedChords = recorded;
        try
        {
            action();
        }
        finally
        {
            InputSimulator.RecordedChords = previous;
        }
        return Assert.Single(recorded);
    }

    [Fact]
    public void SendCopyChord_sends_CtrlC_for_ordinary_apps()
    {
        var sent = Capture(() => InputSimulator.SendCopyChord(CopyPasteChord.CtrlCV));
        Assert.Equal((NativeMethods.VK_CONTROL, NativeMethods.VK_C), sent);
    }

    [Fact]
    public void SendCopyChord_sends_CtrlInsert_for_terminals()
    {
        var sent = Capture(() => InputSimulator.SendCopyChord(CopyPasteChord.InsertChord));
        Assert.Equal((NativeMethods.VK_CONTROL, NativeMethods.VK_INSERT), sent);
    }

    [Fact]
    public void SendCopyChord_sends_nothing_for_copy_on_select_apps()
    {
        var sent = Capture(() => InputSimulator.SendCopyChord(CopyPasteChord.CopyOnSelect));
        Assert.Null(sent);
    }

    [Fact]
    public void SendPasteChord_sends_CtrlV_for_ordinary_apps()
    {
        var sent = Capture(() => InputSimulator.SendPasteChord(CopyPasteChord.CtrlCV));
        Assert.Equal((NativeMethods.VK_CONTROL, NativeMethods.VK_V), sent);
    }

    [Fact]
    public void SendPasteChord_sends_ShiftInsert_for_terminals()
    {
        var sent = Capture(() => InputSimulator.SendPasteChord(CopyPasteChord.InsertChord));
        Assert.Equal((NativeMethods.VK_SHIFT, NativeMethods.VK_INSERT), sent);
    }

    [Fact]
    public void SendPasteChord_sends_ShiftInsert_for_copy_on_select_apps()
    {
        // PuTTY/KiTTY copy on select but still take Shift+Insert to paste, like every other terminal.
        var sent = Capture(() => InputSimulator.SendPasteChord(CopyPasteChord.CopyOnSelect));
        Assert.Equal((NativeMethods.VK_SHIFT, NativeMethods.VK_INSERT), sent);
    }
}
