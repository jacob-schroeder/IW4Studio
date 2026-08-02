using System.Collections.ObjectModel;
using IW4.Studio.Documents;
using IW4.Studio.MapEditor.Editing.Documents;
using IW4.Studio.MapEditor.Editing.Identity;
using IW4.Studio.MapEditor.Editing.Objects;

namespace IW4.Studio.MapEditor.Compilation.StaticModels;

/// <summary>
/// Semantic role played by one exact compiled-record binding in a
/// static-model removal. A provider carry-forward mutates the retained Gfx
/// receiver's nested-XModel ownership, so that row is explicit authority
/// rather than an incidental third binding.
/// </summary>
public enum StaticModelRemovalSourceBindingRole
{
    RemovedGfxRow,
    RemovedCollisionRow,
    GfxProviderReceiverRow
}

public sealed record StaticModelRemovalSourceBindingAuthority(
    StaticModelRemovalSourceBindingRole Role,
    MapObjectId ObjectId,
    SourceBindingId SourceBinding,
    int SourceOrdinal);

internal static class StaticModelRemovalBindingAuthorityResolver
{
    public static IReadOnlyList<StaticModelRemovalSourceBindingAuthority>
        Resolve(
            EditorMapDocument document,
            StaticModelRemovalEligibilityAssessment eligibility)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(eligibility);
        if (!eligibility.IsPatchEligible)
        {
            throw new InvalidOperationException(
                "Static-model removal binding authority requires an " +
                "eligible exact-bundle assessment.");
        }

        StaticModelCompilationRelationship relationship =
            eligibility.Relationship;
        EditorStaticModel render =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.RenderObjectId);
        EditorStaticModel collision =
            document.GetRequiredObject<EditorStaticModel>(
                relationship.CollisionObjectId);
        var authorities =
            new List<StaticModelRemovalSourceBindingAuthority>
            {
                Authority(
                    StaticModelRemovalSourceBindingRole.RemovedGfxRow,
                    render,
                    StaticModelRepresentation.Render,
                    relationship.GfxSourceOrdinal),
                Authority(
                    StaticModelRemovalSourceBindingRole.RemovedCollisionRow,
                    collision,
                    StaticModelRepresentation.Collision,
                    relationship.ClipSourceOrdinal)
            };

        foreach (GfxStaticModelProviderCarryForward carryForward in
                 eligibility.Gfx?.ProviderCarryForwards ?? [])
        {
            if (carryForward.RemovedProviderOrdinal !=
                relationship.GfxSourceOrdinal)
            {
                throw new InvalidOperationException(
                    "The provider carry-forward does not belong to the " +
                    "authorized removed Gfx row.");
            }

            EditorStaticModel[] receivers = document.StaticModels
                .Where(value =>
                    value.IsImported &&
                    value.Representation ==
                        StaticModelRepresentation.Render &&
                    value.SourceOrdinal.Value ==
                        carryForward.ReceiverOrdinal)
                .ToArray();
            if (receivers.Length != 1)
            {
                throw new InvalidOperationException(
                    "The exact adjacent Gfx provider receiver cannot be " +
                    "resolved uniquely in the imported document.");
            }
            authorities.Add(Authority(
                StaticModelRemovalSourceBindingRole
                    .GfxProviderReceiverRow,
                receivers[0],
                StaticModelRepresentation.Render,
                carryForward.ReceiverOrdinal));
        }

        if (authorities.Select(value => value.SourceBinding)
                .Distinct().Count() != authorities.Count)
        {
            throw new InvalidOperationException(
                "Static-model removal binding roles do not resolve to " +
                "distinct exact record authorities.");
        }
        return new ReadOnlyCollection<
            StaticModelRemovalSourceBindingAuthority>(
            authorities);
    }

    private static StaticModelRemovalSourceBindingAuthority Authority(
        StaticModelRemovalSourceBindingRole role,
        EditorStaticModel model,
        StaticModelRepresentation expectedRepresentation,
        int expectedOrdinal)
    {
        if (!model.IsImported ||
            model.Representation != expectedRepresentation ||
            model.SourceOrdinal.Value != expectedOrdinal)
        {
            throw new InvalidOperationException(
                $"Static-model removal binding role {role} does not match " +
                "the exact imported representation and source ordinal.");
        }
        return new(
            role,
            model.Id,
            model.SourceOrdinal.SourceBinding,
            expectedOrdinal);
    }
}
