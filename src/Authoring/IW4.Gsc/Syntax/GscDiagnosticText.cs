using System.Text;

namespace IW4.Gsc.Syntax;

internal static class GscDiagnosticText
{
    private const int MaximumDisplayedCharacters = 32;

    internal static string Quote(string text)
    {
        var result = new StringBuilder();
        result.Append('\'');

        int count = Math.Min(text.Length, MaximumDisplayedCharacters);
        for (int index = 0; index < count; index++)
        {
            result.Append(text[index] switch
            {
                '\0' => "\\0",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\'' => "\\'",
                '\\' => "\\\\",
                _ => text[index].ToString()
            });
        }

        if (text.Length > count)
            result.Append('…');
        result.Append('\'');
        return result.ToString();
    }
}
