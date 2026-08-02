using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxSoundVisual : FxElemVisual
{
    public XPointer<string> SoundNamePointer { get; init; }
    public string? SoundName { get; init; }
}
