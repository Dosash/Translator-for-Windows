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
            var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true };
            Console.SetError(stderr);
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
        }
        catch (Exception ex)
        {
            DebugLog.Write($"ConsoleAttach: reopening streams failed: {ex.GetType().Name}");
        }
        return true;
    }
}
