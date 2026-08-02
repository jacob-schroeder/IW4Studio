using IW4.Assets.Assets.Fx;
using IW4.FastFiles.Pointers;

namespace IW4.FastFiles.Loaders.Assets.Fx;

internal sealed record FxVisualPayload(
    FxElemDefVisuals? InlineVisual,
    XPointer<FxElemDefVisuals[]>? VisualArrayPointer,
    IReadOnlyList<FxElemDefVisuals> VisualArray,
    XPointer<FxElemMarkVisuals[]>? MarkVisualArrayPointer,
    IReadOnlyList<FxElemMarkVisuals> MarkVisualArray);
