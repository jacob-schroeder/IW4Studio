using IW4.FastFiles.Pointers;

namespace IW4.FastFiles.Loaders.Assets.Fx;

internal sealed record FxElemDefVisualsRoot(
    int Offset,
    XPointer<object> Raw);
