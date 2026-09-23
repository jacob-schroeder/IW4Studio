using IW4.Game.Database;

namespace IW4.Linker.Packaging;

/// <summary>
/// Authored PS3 fastfile header values that are independent of the rebuilt
/// language tables and derived package sizes.
/// </summary>
public sealed record DbHeaderAuthoringMetadata(
    string Magic,
    XFileVersion Version,
    bool AllowOnlineUpdate,
    ulong FileCreationTimeRaw,
    uint MaxFileSize)
{
    /// <summary>The canonical values for a new PS3 IW4 fastfile.</summary>
    public static DbHeaderAuthoringMetadata Canonical { get; } = new(
        DbHeader.UnsignedMagic,
        XFileVersion.ModernWarfare2,
        AllowOnlineUpdate: false,
        FileCreationTimeRaw: 0,
        MaxFileSize: 0);

    public bool HasSupportedMagic => IsSupportedMagic(Magic);

    public bool HasSupportedVersion => IsSupportedVersion(Version);

    public static bool IsSupportedMagic(string? magic) =>
        string.Equals(magic, DbHeader.UnsignedMagic, StringComparison.Ordinal);

    public static bool IsSupportedVersion(XFileVersion version) =>
        version == XFileVersion.ModernWarfare2;

    public static DbHeaderAuthoringMetadata FromHeader(DbHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return new DbHeaderAuthoringMetadata(
            header.Magic,
            header.Version,
            header.AllowOnlineUpdate,
            header.FileCreationTimeRaw,
            header.MaxFileSize);
    }
}
