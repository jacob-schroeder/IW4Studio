namespace Iw4Radiant.Views;

internal static class FileOperationErrors
{
    internal static bool IsExpected(Exception exception) => exception is IOException or UnauthorizedAccessException or FormatException or
        ArgumentException or InvalidOperationException or NotSupportedException or OverflowException;
}
