using System.Runtime.InteropServices;

namespace SISRWatcher;

internal static class ClipboardHelper
{
    /// <summary>Copies the lines to the clipboard, one per line. Does nothing if there are none.</summary>
    public static void CopyLines(IEnumerable<string> lines)
    {
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0) return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Another program was using the clipboard at that moment. Nothing useful to do.
        }
    }
}
