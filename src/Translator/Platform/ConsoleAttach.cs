using System.IO;
using System.Text;
using Translator.Core;

namespace Translator.Platform;

/// <summary>Attaches to the launching console (cmd/PowerShell) so CLI test modes print where the user is looking.</summary>
public static class ConsoleAttach
{
    public static bool AttachToParent()
    {
        if (!NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
        {
            return false;
        }
        try
        {
            // No BOM: redirected output (scripts, CI) would otherwise start with U+FEFF.
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true };
            Console.SetError(stderr);
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ConsoleAttach: reopening streams failed: {ex.GetType().Name}");
        }
        return true;
    }
}
