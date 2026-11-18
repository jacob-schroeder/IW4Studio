using System.Security.Cryptography;
using System.Text;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Persistence;

internal static class ShaderSourceStore
{
    private static readonly UTF8Encoding SourceEncoding = new(false, true);

    internal static string? Read(ShaderDraft draft)
    {
        if (draft.Bytecode.IsEmpty) return null;
        try
        {
            using var stream = new FileStream(GetPath(draft), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            long length = stream.Length;
            if (length <= SHA256.HashSizeInBytes || length > Array.MaxLength) return null;

            Span<byte> checksum = stackalloc byte[SHA256.HashSizeInBytes];
            stream.ReadExactly(checksum);
            byte[] source = new byte[(int)length - SHA256.HashSizeInBytes];
            stream.ReadExactly(source);
            return checksum.SequenceEqual(SHA256.HashData(source)) ? SourceEncoding.GetString(source) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    internal static void Write(ShaderDraft draft, string source)
    {
        byte[] bytes = SourceEncoding.GetBytes(source);
        string path = GetPath(draft);
        string directory = Path.GetDirectoryName(path) ?? throw new IOException("The shader source folder is unavailable.");
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(SHA256.HashData(bytes));
                stream.Write(bytes);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string GetPath(ShaderDraft draft)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(root)) throw new IOException("The per-user application data folder is unavailable.");

        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> stage = [(byte)draft.Kind];
        identity.AppendData(stage);
        // Hash the name separately so the name and program fields have an unambiguous boundary.
        identity.AppendData(SHA256.HashData(Encoding.UTF8.GetBytes(draft.Name ?? string.Empty)));
        identity.AppendData(draft.Bytecode.Span);
        string key = Convert.ToHexString(identity.GetHashAndReset());
        return Path.Combine(root, "IW4Studio", "ShaderSource", key + ".source");
    }
}
