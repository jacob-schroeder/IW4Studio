namespace IW4.Formats.SourceFormat;

internal static class SourceText
{
    public static void WriteQuotedContent(TextWriter writer, string value)
    {
        foreach (char character in value)
        {
            writer.Write(character switch
            {
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                '\f' => "\\f",
                '"' => "\\\"",
                '\\' => "\\\\",
                _ => character.ToString()
            });
        }
    }
}
