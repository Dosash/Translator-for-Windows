using Translator.Core;

namespace Translator.Platform;

/// <summary>Synthesizes Ctrl+C / Ctrl+V via SendInput, careful not to echo the caller's own hotkey chord.</summary>
internal static class InputSimulator
{
    private static readonly int[] ModifierVks =
    [
        NativeMethods.VK_CONTROL, NativeMethods.VK_MENU, NativeMethods.VK_SHIFT,
        NativeMethods.VK_LWIN, NativeMethods.VK_RWIN,
    ];

    /// <summary>
    /// Waits up to 1.5 s for the user to release Ctrl/Alt/Shift/Win (so a hotkey chord isn't echoed
    /// into the simulated Ctrl+C/V). If still held, forces the release.
    /// </summary>
    public static async Task WaitForModifiersReleasedAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < 30; i++)
        {
            if (!AnyModifierHeld())
            {
                return;
            }
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        if (AnyModifierHeld())
        {
            DebugLog.Write("InputSimulator: modifiers still held after 1.5s, forcing release");
            ForceReleaseModifiers();
        }
    }

    /// <summary>Copy for <paramref name="chord"/>'s profile. Sends nothing for <see cref="CopyPasteChord.CopyOnSelect"/> —
    /// those apps already copied on selection, so there is no keystroke to send.</summary>
    public static void SendCopyChord(CopyPasteChord chord)
    {
        switch (chord)
        {
            case CopyPasteChord.CtrlCV:
                SendChord(NativeMethods.VK_CONTROL, NativeMethods.VK_C);
                break;
            case CopyPasteChord.InsertChord:
                SendChord(NativeMethods.VK_CONTROL, NativeMethods.VK_INSERT);
                break;
            case CopyPasteChord.CopyOnSelect:
                RecordedChords?.Add(null);
                break;
        }
    }

    /// <summary>Paste for <paramref name="chord"/>'s profile.</summary>
    public static void SendPasteChord(CopyPasteChord chord)
    {
        switch (chord)
        {
            case CopyPasteChord.CtrlCV:
                SendChord(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
                break;
            case CopyPasteChord.InsertChord:
            case CopyPasteChord.CopyOnSelect:
                SendChord(NativeMethods.VK_SHIFT, NativeMethods.VK_INSERT);
                break;
        }
    }

    /// <summary>
    /// Test seam: when set, chords are recorded here as (modifier, key) instead of really being sent —
    /// so a "no key was sent" (<see cref="CopyPasteChord.CopyOnSelect"/> copy) can be asserted too.
    /// </summary>
    internal static List<(int Modifier, int Vk)?>? RecordedChords { get; set; }

    private static bool AnyModifierHeld()
    {
        foreach (var vk in ModifierVks)
        {
            if (IsDown(vk))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void ForceReleaseModifiers()
    {
        var inputs = new List<NativeMethods.INPUT>
        {
            // A harmless keystroke first, so a lone Alt key-up isn't read as "open the menu".
            KeyInput(NativeMethods.VK_DUMMY, down: true),
            KeyInput(NativeMethods.VK_DUMMY, down: false),
        };
        foreach (var vk in ModifierVks)
        {
            if (IsDown(vk))
            {
                inputs.Add(KeyInput(vk, down: false));
            }
        }
        SendInputs(inputs);
    }

    private static void SendChord(int modifierVk, int vk)
    {
        if (RecordedChords is { } recorded)
        {
            recorded.Add((modifierVk, vk));
            return;
        }
        SendInputs(
        [
            KeyInput(modifierVk, down: true),
            KeyInput(vk, down: true),
            KeyInput(vk, down: false),
            KeyInput(modifierVk, down: false),
        ]);
    }

    private static NativeMethods.INPUT KeyInput(int vk, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = 0,
                dwFlags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static void SendInputs(IReadOnlyList<NativeMethods.INPUT> inputs)
    {
        var array = inputs as NativeMethods.INPUT[] ?? [.. inputs];
        var sent = NativeMethods.SendInput((uint)array.Length, array, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != array.Length)
        {
            DebugLog.Write($"InputSimulator: SendInput sent {sent}/{array.Length}");
        }
    }
}
