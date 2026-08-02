using System.Text;

namespace IW4.FastFiles.Database;

// DBFile contains a Sys_File handle and a 64-byte single-byte name buffer.
// Managed references below do not expose a native pointer layout.
public sealed class DbFile
{
    public const int NameCapacity = 64;

    public DbFile(SysFile sysFile, string name)
    {
        SysFile = sysFile ?? throw new ArgumentNullException(nameof(sysFile));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.IndexOf('\0') >= 0)
            throw new ArgumentException("DBFile names cannot contain an embedded null.", nameof(name));
        if (name.Any(character => character > byte.MaxValue))
        {
            throw new ArgumentException(
                "DBFile names must be representable by the engine's single-byte name buffer.",
                nameof(name));
        }

        int encodedLength = Encoding.Latin1.GetByteCount(name);
        if (encodedLength >= NameCapacity)
        {
            throw new ArgumentException(
                $"DBFile names must fit in the {NameCapacity}-byte engine buffer.",
                nameof(name));
        }

        Name = name;
    }

    public SysFile SysFile { get; }

    public string Name { get; }
}
