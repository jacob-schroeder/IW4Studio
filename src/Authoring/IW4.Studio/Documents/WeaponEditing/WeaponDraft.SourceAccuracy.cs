using IW4.Game.Assets.Weapon;
using IW4.Game.Math;
using IW4.Game.Pointers;

namespace IW4.Studio.Documents;

public sealed partial class WeaponDraft
{
    internal void SetSourceAccuracyGraph(bool aiVsAi, string? name,
        IReadOnlyList<Vec2> knots)
    {
        ArgumentNullException.ThrowIfNull(knots);
        if (name is null && knots.Count != 0)
            throw new ArgumentException("A cleared accuracy graph cannot retain knots.", nameof(knots));
        if (knots.Count > ushort.MaxValue || knots.Any(knot =>
                !float.IsFinite(knot.a) || !float.IsFinite(knot.b)))
            throw new InvalidDataException("Accuracy graph knots are invalid or exceed the native count.");

        WeaponAccuracyFields current = RequireDefinition().Accuracy;
        IReadOnlyList<Vec2> detached = Array.AsReadOnly(knots.ToArray());
        var replacement = new WeaponAccuracyFields
        {
            AiVsAiGraphNamePointer = aiVsAi
                ? name is null ? default : new XPointer<string>(-1)
                : current.AiVsAiGraphNamePointer,
            AiVsAiGraphName = aiVsAi ? name : current.AiVsAiGraphName,
            AiVsPlayerGraphNamePointer = !aiVsAi
                ? name is null ? default : new XPointer<string>(-1)
                : current.AiVsPlayerGraphNamePointer,
            AiVsPlayerGraphName = aiVsAi ? current.AiVsPlayerGraphName : name,
            OriginalAiVsAiGraphKnotsPointer = aiVsAi
                ? name is null ? default : new XPointer<Vec2[]>(-1)
                : current.OriginalAiVsAiGraphKnotsPointer,
            OriginalAiVsAiGraphKnots = aiVsAi ? detached : current.OriginalAiVsAiGraphKnots,
            OriginalAiVsPlayerGraphKnotsPointer = !aiVsAi
                ? name is null ? default : new XPointer<Vec2[]>(-1)
                : current.OriginalAiVsPlayerGraphKnotsPointer,
            OriginalAiVsPlayerGraphKnots = aiVsAi ? current.OriginalAiVsPlayerGraphKnots : detached,
            OriginalAiVsAiGraphKnotCount = aiVsAi
                ? checked((ushort)detached.Count) : current.OriginalAiVsAiGraphKnotCount,
            OriginalAiVsPlayerGraphKnotCount = !aiVsAi
                ? checked((ushort)detached.Count) : current.OriginalAiVsPlayerGraphKnotCount,
            PositionReloadTransitionTime = current.PositionReloadTransitionTime,
            LeftArc = current.LeftArc,
            RightArc = current.RightArc,
            TopArc = current.TopArc,
            BottomArc = current.BottomArc,
            Accuracy = current.Accuracy,
            AiSpread = current.AiSpread,
            PlayerSpread = current.PlayerSpread
        };
        WeaponDef definition = CopyWeaponDefAccuracy(RequireDefinition(), replacement);
        _asset = new WeaponAsset
        {
            Variant = CopyVariantWithDefinition(_asset.Variant, definition,
                aiVsAi, detached, name)
        };
    }
}
