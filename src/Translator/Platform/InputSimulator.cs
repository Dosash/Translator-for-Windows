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

    public static void SendCtrlC() => SendChord(NativeMethods.VK_C);

    public static void SendCtrlV() => SendChord(NativeMethods.VK_V);

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

    private static void SendChord(int vk)
    {
        SendInputs(
        [
            KeyInput(NativeMethods.VK_CONTROL, down: true),
            KeyInput(vk, down: true),
            KeyInput(vk, down: false),
            KeyInput(NativeMethods.VK_CONTROL, down: false),
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
