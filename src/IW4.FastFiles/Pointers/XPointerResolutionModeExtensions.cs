using IW4.FastFiles.Zone;

namespace IW4.FastFiles.Pointers;

public static class XPointerResolutionModeExtensions
{
    public static XPointerResolutionMode ToResolutionMode(this XPointerOffsetMode mode)
    {
        return mode switch
        {
            XPointerOffsetMode.None => XPointerResolutionMode.None,
            XPointerOffsetMode.Direct => XPointerResolutionMode.Direct,
            XPointerOffsetMode.AliasCell => XPointerResolutionMode.AliasCell,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown pointer resolution mode.")
        };
    }

    public static XPointerOffsetMode ToOffsetMode(this XPointerResolutionMode mode)
    {
        return mode switch
        {
            XPointerResolutionMode.None => XPointerOffsetMode.None,
            XPointerResolutionMode.Direct => XPointerOffsetMode.Direct,
            XPointerResolutionMode.AliasCell => XPointerOffsetMode.AliasCell,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown pointer resolution mode.")
        };
    }
}
