using System.Text;

namespace Wintodo.Core.Import;

/// <summary>Marks downloaded files without storing source URLs or account metadata.</summary>
public static class DownloadedAttachmentProvenance
{
    public static void MarkInternet(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            return;

        // Writing an alternate stream can otherwise create an empty unnamed file.
        // Callers must own a completed file before marking or copying it.
        if (!File.Exists(path))
            throw new FileNotFoundException("The downloaded attachment is missing.");

        try
        {
            File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", Encoding.ASCII);
        }
        catch (IOException exception) when (exception.HResult is unchecked((int)0x80070001) or unchecked((int)0x80070032))
        {
            // ERROR_INVALID_FUNCTION / ERROR_NOT_SUPPORTED: the filesystem does
            // not support named streams. Attachment opening still uses Windows
            // Launcher with TreatAsUntrusted. Other IO/permission errors fail the
            // import and its caller removes only the files created by that attempt.
        }
    }
}
