using System.Numerics;
using IW4.Formats.SourceFormat.Character;
using IW4.Formats.XModel;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.XModel;

namespace IW4.Studio.Documents;

/// <summary>Compiles a compatible skinned GLB against one retained stock character model.</summary>
public static class XModelCharacterImporter
{
    private const int MaxFullDetailAttempts = 5;

    public static XModelAssemblyCompileResult Compile(
        XModelAsset template,
        XModelExportDocument imported,
        string modelName,
        string source,
        bool viewHands)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        if (template.CollLod != 0xFF || template.CollSurfs.Count != 0)
            throw new InvalidDataException("The selected character template has visual collision data that cannot be retained with new skinned geometry.");
        if (!XModelExportSkeletonProjector.TryProject(template,
                out IReadOnlyList<XModelExportBone> targetBones,
                out IReadOnlyList<string> blockers))
            throw new InvalidDataException(string.Join(" ", blockers));

        XModelExportDocument remapped = RemapSkeleton(imported, targetBones);
        // Native surface count spans every LOD. Merge source mesh objects by material
        // before generating and compiling the distance LODs.
        var document = new XModelExportDocument(
            remapped.Bones,
            remapped.Vertices,
            Array.AsReadOnly(remapped.Triangles.Select(triangle => triangle with { ObjectIndex = 0 }).ToArray()),
            [new XModelExportObject("character")],
            remapped.Materials);
        CharacterLodCounts limits = CharacterModelBudget.Limits(viewHands);
        int sourceSections = document.Triangles.Select(triangle =>
            (triangle.ObjectIndex, triangle.MaterialIndex)).Distinct().Count();
        if (sourceSections > limits.Sections)
            throw new InvalidDataException(
                $"Character '{modelName}' has {sourceSections} source material sections; the PS3 authoring limit is {limits.Sections}. " +
                "Merge material sections in Blender, then re-import.");

        // The native count includes emitted UV/normal/tangent splits. Measure each
        // candidate after compilation, while deriving every retry from the source.
        XModelExportLodCompileResult fullDetail = XModelExportLodCompiler.Compile(
            document, template.NumBones, compileCollisionTrees: false);
        if (!fullDetail.IsSuccess)
            throw new InvalidDataException(string.Join(" ", fullDetail.Blockers));
        CharacterLodCounts originalCounts = CharacterModelBudget.Count(fullDetail.Surfaces);
        XModelExportDocument lod0 = document;
        if (!CharacterModelBudget.Fits(originalCounts, limits))
        {
            CharacterLodCounts? lastCompiledCounts = null;
            XModelExportDocument? bestFit = null;
            string? lastCompileBlocker = null;
            int lastCandidateTriangles = 0;
            double fittingRatio = 0;
            double failingRatio = 1;
            double targetRatio = Math.Min(0.95,
                Math.Min((double)limits.Vertices / originalCounts.Vertices,
                    (double)limits.Triangles / originalCounts.Triangles)) * 0.95;
            for (int attempt = 0; attempt < MaxFullDetailAttempts; attempt++)
            {
                XModelExportDocument candidate;
                try
                {
                    candidate = XModelCharacterLodGenerator.ReduceFullDetail(document, (float)targetRatio);
                }
                catch (InvalidDataException) when (bestFit is not null)
                {
                    // A higher-detail retry must not discard an already valid fit.
                    break;
                }
                lastCandidateTriangles = candidate.Triangles.Count;
                XModelExportLodCompileResult native = XModelExportLodCompiler.Compile(
                    candidate, template.NumBones, compileCollisionTrees: false);
                if (!native.IsSuccess)
                {
                    if (bestFit is not null)
                        break;
                    lastCompileBlocker = string.Join(Environment.NewLine, native.Blockers);
                    targetRatio *= 0.8;
                    continue;
                }
                CharacterLodCounts counts = CharacterModelBudget.Count(native.Surfaces);
                lastCompiledCounts = counts;
                if (CharacterModelBudget.Fits(counts, limits))
                {
                    if (bestFit is null || candidate.Triangles.Count > bestFit.Triangles.Count)
                        bestFit = candidate;
                    fittingRatio = Math.Max(fittingRatio, targetRatio);
                }
                else if (targetRatio > fittingRatio)
                {
                    failingRatio = Math.Min(failingRatio, targetRatio);
                }
                if (fittingRatio > 0)
                    targetRatio = (fittingRatio + failingRatio) / 2;
                else
                    targetRatio *= Math.Min(0.9,
                        Math.Min((double)limits.Vertices / counts.Vertices,
                            (double)limits.Triangles / counts.Triangles)) * 0.95;
                if (targetRatio <= 0 || targetRatio >= 1 ||
                    Math.Abs(failingRatio - fittingRatio) < 0.0001)
                    break;
            }
            if (bestFit is null)
            {
                if (lastCompiledCounts is not { } counts)
                    throw new InvalidDataException(
                        $"Automatic optimization could not create valid LOD0 geometry for '{modelName}'. " +
                        $"The last attempt contained {lastCandidateTriangles:N0} triangles, but could not be compiled for PS3. " +
                        "A game vertex count is unavailable for that result." +
                        $"{Environment.NewLine}{Environment.NewLine}{lastCompileBlocker}");
                throw new InvalidDataException(
                    $"Automatic optimization could not fit '{modelName}' within the PS3 LOD0 budget while preserving detail. " +
                    $"Last successfully compiled result: {counts.Vertices:N0}/{limits.Vertices:N0} vertices, " +
                    $"{counts.Triangles:N0}/{limits.Triangles:N0} triangles, {counts.Sections}/{limits.Sections} mesh sections. " +
                    "Simplify dense geometry or reduce material sections in Blender, then re-import." +
                    (lastCompileBlocker is null ? "" :
                        $"{Environment.NewLine}{Environment.NewLine}Some attempts also could not be compiled:{Environment.NewLine}{lastCompileBlocker}"));
            }
            lod0 = bestFit;
        }

        IReadOnlyList<XModelExportDocument> documents = XModelCharacterLodGenerator.Generate(lod0, template.NumLods);
        var draft = new XModelDraft(template, modelName);
        for (int lod = 0; lod < documents.Count; lod++)
        {
            draft.ReplaceLod(lod, documents[lod], source);
            for (int materialIndex = 0; materialIndex < document.Materials.Count; materialIndex++)
            {
                XModelExportMaterial material = document.Materials[materialIndex];
                XModelMaterialMapping mapping = SelectMaterialTemplate(template, material);
                draft.SetImportedMaterialMapping(lod, materialIndex, mapping);
            }
        }
        draft.RebuildCharacterVisualBounds();
        XModelAssemblyCompileResult compiled = XModelAssemblyCompiler.Compile(draft);
        if (!compiled.IsSuccess)
            throw new InvalidDataException(string.Join(" ", compiled.Issues
                .Where(issue => issue.Severity == AssetValidationSeverity.Error)
                .Select(issue => $"{issue.FieldPath}: {issue.Message}")));
        if (viewHands)
            CharacterModelBudget.ValidateHands(compiled.Definition);
        else
            CharacterModelBudget.ValidateBody(compiled.Definition);
        return compiled;
    }

    private static XModelMaterialMapping SelectMaterialTemplate(
        XModelAsset template,
        XModelExportMaterial imported)
    {
        for (int index = 0; index < template.Materials.Count && index < template.InvHighMipRadius.Count; index++)
        {
            MaterialAsset? material = template.Materials[index];
            if (material is not null &&
                XModelImportedMaterialCompiler.IsCompatibleImportTemplate(imported, material, out _))
                return new XModelMaterialMapping(material, template.InvHighMipRadius[index]);
        }
        throw new InvalidDataException($"No compatible stock IW4 material template exists for GLB material '{imported.Name}'.");
    }

    private static XModelExportDocument RemapSkeleton(
        XModelExportDocument imported,
        IReadOnlyList<XModelExportBone> target)
    {
        if (imported.Bones.Count != target.Count)
            throw new InvalidDataException($"The GLB has {imported.Bones.Count} joints; the character template requires {target.Count}.");
        var targetByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < target.Count; index++)
            if (!targetByName.TryAdd(target[index].Name, index))
                throw new InvalidDataException($"The character template repeats joint '{target[index].Name}'.");
        var sourceByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var targetIndexBySource = new int[imported.Bones.Count];
        for (int index = 0; index < imported.Bones.Count; index++)
        {
            XModelExportBone bone = imported.Bones[index];
            if (!sourceByName.TryAdd(bone.Name, index) || !targetByName.TryGetValue(bone.Name, out int targetIndex))
                throw new InvalidDataException($"The GLB has a duplicate or unknown joint '{bone.Name}'.");
            targetIndexBySource[index] = targetIndex;
        }
        for (int index = 0; index < imported.Bones.Count; index++)
        {
            XModelExportBone source = imported.Bones[index];
            XModelExportBone expected = target[targetIndexBySource[index]];
            int parent = source.ParentIndex == -1 ? -1 :
                (uint)source.ParentIndex < (uint)imported.Bones.Count
                    ? targetIndexBySource[source.ParentIndex]
                    : throw new InvalidDataException($"GLB joint '{source.Name}' has an invalid parent index.");
            if (parent != expected.ParentIndex || !SamePose(source, expected))
                throw new InvalidDataException($"GLB joint '{source.Name}' does not match the stock parent and bind pose.");
        }
        XModelExportVertex[] vertices = imported.Vertices.Select(vertex => new XModelExportVertex(
            vertex.Position,
            Array.AsReadOnly(vertex.Weights.Select(weight =>
            {
                if ((uint)weight.BoneIndex >= (uint)targetIndexBySource.Length)
                    throw new InvalidDataException("A GLB vertex references a missing skin joint.");
                return new XModelExportBoneWeight(targetIndexBySource[weight.BoneIndex], weight.Weight);
            }).ToArray()))).ToArray();
        return new XModelExportDocument(
            target,
            Array.AsReadOnly(vertices),
            imported.Triangles,
            imported.Objects,
            imported.Materials);
    }

    private static bool SamePose(XModelExportBone left, XModelExportBone right)
    {
        const float tolerance = 0.0005f;
        if (Vector3.Distance(left.GlobalOffset, right.GlobalOffset) > tolerance ||
            !float.IsFinite(left.GlobalRotation.LengthSquared()) ||
            !float.IsFinite(right.GlobalRotation.LengthSquared()) ||
            left.GlobalRotation.LengthSquared() <= 0f || right.GlobalRotation.LengthSquared() <= 0f)
            return false;
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(left.GlobalRotation), Quaternion.Normalize(right.GlobalRotation)));
        return 1f - dot <= tolerance;
    }
}
