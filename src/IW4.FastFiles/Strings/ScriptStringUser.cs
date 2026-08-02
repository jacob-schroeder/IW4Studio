using IW4.FastFiles.Zone;
namespace IW4.FastFiles.Strings;

/// <summary>
/// SL string ownership bits used by the loader. Only the XZone bit is named.
/// </summary>
[Flags]
public enum ScriptStringUser : uint
{
    None = 0,
    XZone = 4
}
