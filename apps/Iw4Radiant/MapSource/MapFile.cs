using System.Text;
using Iw4Radiant.MapSource.Parsing;

namespace Iw4Radiant.MapSource;

internal static class MapFile
{
    public static MapDocument Read(string path)
    {
        try
        {
            return new MapReader(File.ReadAllText(path, new UTF8Encoding(false, true))).Read();
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException($"{Path.GetFileName(path)}: The source contains invalid text encoding; convert it to UTF-8 before opening.", exception);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"{Path.GetFileName(path)}: {exception.Message}", exception);
        }
    }

    public static void Write(MapDocument document, string path)
    {
        string contents = MapWriter.Serialize(document);
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination) ?? ".",
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
                    writer.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
