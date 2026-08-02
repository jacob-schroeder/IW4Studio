using IW4.Assets.Assets.Material;
using IW4.Assets.Assets.XModel;
using IW4.FastFiles.Pointers;

namespace IW4.Assets.Assets.Fx;

public sealed class FxEffectDefRef
{
    public XPointer<string> NamePointer { get; init; }
    public string? Name { get; init; }
}
