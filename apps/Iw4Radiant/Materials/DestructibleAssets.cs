using System.Text.Json;
using IW4.Formats.SourceFormat.Fx;
using IW4.Formats.SourceFormat.Material;
using IW4.Formats.SourceFormat.PhysCollmap;
using IW4.Formats.SourceFormat.PhysPreset;
using IW4.Formats.SourceFormat.Sound;
using IW4.Formats.SourceFormat.XModel;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.Fx;
using IW4.Game.Assets.Sound;
using Iw4Radiant.Editing;

namespace Iw4Radiant.Materials;

internal sealed record DestructibleReadiness(IReadOnlyList<string> Issues, bool HasMissingAssets)
{
    internal bool IsReady => Issues.Count == 0;
    internal string Label => IsReady ? "Ready" : HasMissingAssets ? "Missing assets" : "Incompatible assets";
}

/// <summary>Checks preset source graphs using the same format readers as native linking.</summary>
internal static class DestructibleAssets
{
    internal static IReadOnlyDictionary<DestructiblePreset, DestructibleReadiness> Check(
        string sourceDirectory, IEnumerable<DestructiblePreset> presets, CancellationToken cancellationToken,
        string? bootstrapDirectory = null)
    {
        string root = Path.GetFullPath(sourceDirectory);
        string bootstrap = bootstrapDirectory ?? Path.Combine(AppContext.BaseDirectory, "bootstrap", "ps3");
        var materials = new MaterialSourceCompiler(root, bootstrap);
        var results = new Dictionary<DestructiblePreset, DestructibleReadiness>();
        // Retain leaf outcomes between presets; each FX/sound traversal keeps its own cycle guard.
        var checkedAssets = new Dictionary<(string Kind, string Name), DestructibleReadiness>();
        var modelTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (DestructiblePreset preset in presets.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issues = new List<string>();
            bool missing = false;
            var visiting = new HashSet<(string, string)>();
            foreach (string name in preset.ModelNames) Include("Model", name);
            foreach (string name in preset.FxNames) Include("FX", name);
            foreach (string name in preset.SoundNames) Include("Sound", name);
            foreach (string name in preset.AnimationNames) Include("XAnim", name);
            if (preset.PrecacheRawFileName is { } helper)
            {
                Include("RawFile", helper);
                Include("RawFile", "animtrees/destructibles.atr");
            }
            if (modelTags.TryGetValue(preset.ModelName, out var tags))
            {
                var requiredTags = preset.Preview.Stages.Select(stage => stage.FxTag)
                    .Concat((preset.Preview.Parts ?? []).SelectMany(part => new[] { part.IntactTag, part.DamagedTag, part.FxTag }))
                    .OfType<string>().Distinct(StringComparer.Ordinal);
                foreach (string tag in requiredTags)
                    if (!tags.Contains(tag)) issues.Add($"Model '{preset.ModelName}' is missing required attachment '{tag}'.");
            }
            results.Add(preset, new(issues.Distinct(StringComparer.Ordinal).ToArray(), missing));

            void Include(string kind, string? reference)
            {
                if (string.IsNullOrWhiteSpace(reference)) return;
                cancellationToken.ThrowIfCancellationRequested();
                string name = reference.TrimStart(',');
                var key = (kind, name);
                if (!visiting.Add(key)) return;
                if (kind is "Model" or "Material" && checkedAssets.TryGetValue(key, out DestructibleReadiness? cached))
                {
                    issues.AddRange(cached.Issues);
                    missing |= cached.HasMissingAssets;
                    return;
                }
                int start = issues.Count;
                bool assetMissing = false;
                try
                {
                    switch (kind)
                    {
                        case "Model":
                            var imported = new XModelNativeExchange().Link(SourceRoot($"xmodel_native/{name}.json"), name,
                                materials.LoadMaterial,
                                physics => new PhysPresetExchange().Link(SourceRoot($"physic/{physics}.physic.json"), physics),
                                collision => new PhysCollmapExchange().Link(SourceRoot($"phys_collmaps/{collision}.phys_collmap.json"), collision));
                            modelTags[name] = imported.Model.BoneNames.Select(bone => bone.Text).OfType<string>().ToHashSet(StringComparer.Ordinal);
                            break;
                        case "Material":
                            materials.LoadMaterial(name);
                            break;
                        case "XAnim":
                            new XAnimNativeExchange().Link(SourceRoot($"xanim_native/{name}.json"), name);
                            break;
                        case "RawFile":
                            string path = Path.Combine(SourceRoot(name), name);
                            if (!File.Exists(path))
                                throw new FileNotFoundException($"Missing RawFile '{name}'.", path);
                            break;
                        case "FX":
                            FxEffectDefAsset effect = new FxExchange().Link(root, name);
                            foreach (FxElemDef element in effect.ElemDefs)
                            {
                                Include("FX", element.EffectOnImpact.Name);
                                Include("FX", element.EffectOnDeath.Name);
                                Include("FX", element.EffectEmitted.Name);
                                foreach (FxElemDefVisuals visual in element.VisualArray.Prepend(element.Visuals))
                                {
                                    Include("FX", visual.Effect?.EffectDef.Name);
                                    Include("Sound", visual.Sound?.SoundName);
                                    Include("Model", visual.Model?.Model?.Name);
                                    Include("Material", visual.Material?.Material?.Info.Name);
                                }
                                foreach (FxElemMarkVisuals mark in element.MarkVisualArray)
                                {
                                    Include("Material", mark.Material0?.Info.Name);
                                    Include("Material", mark.Material1?.Info.Name);
                                }
                            }
                            break;
                        case "Sound":
                            // Supplying the callback asks the owning exchange to require and validate
                            // stream payloads. This temporary record is discarded; no package is written.
                            SoundAliasListAsset sound = new SoundAliasListExchange().Link(root, name, bytes => new StreamedSound
                            {
                                FileIndex = StreamedSound.NamedFileIndex,
                                Source = new StreamedSoundFileSource { StreamFileLength = bytes.Length }
                            });
                            foreach (SndAlias alias in sound.Aliases)
                            {
                                Include("Sound", alias.SecondaryAliasName);
                                Include("Sound", alias.ChainAliasName);
                            }
                            break;
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException or OverflowException)
                {
                    assetMissing = exception is FileNotFoundException or DirectoryNotFoundException or
                        MaterialSourceException { IsMissing: true };
                    string detail = exception is FileNotFoundException { FileName: { } path }
                        ? $"Missing {path}" : exception.Message;
                    issues.Add($"{kind} '{name}': {detail}");
                    missing |= assetMissing;
                }
                if (kind is "Model" or "Material")
                    checkedAssets[key] = new(issues.Skip(start).ToArray(), assetMissing);
            }
        }
        return results;

        string SourceRoot(string relative) => !File.Exists(Path.Combine(root, relative)) &&
            File.Exists(Path.Combine(bootstrap, relative)) ? bootstrap : root;
    }
}
