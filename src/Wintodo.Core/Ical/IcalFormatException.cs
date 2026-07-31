namespace Wintodo.Core.Ical;

public sealed class IcalFormatException : FormatException
{
    public IcalFormatException(string message, int lineNumber = 0, Exception? innerException = null)
        : base(lineNumber > 0 ? $"Line {lineNumber}: {message}" : message, innerException)
    {
        LineNumber = lineNumber;
    }

    public int LineNumber { get; }
}
