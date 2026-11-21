using System.Runtime.CompilerServices;
using System.Text;
using IW4.Formats.RawFile;
using IW4.Game.Assets.RawFile;
using IW4.Game.Zone;
using IW4.Gsc.Workspace;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Desktop.Editors.XAnim;

/// <summary>Exact animation uses in loaded script and animtree RawFiles.</summary>
internal static class XAnimScriptReferences
{
    private static readonly ConditionalWeakTable<FastFileWorkspace, Cache> Caches = new();

    internal static IEnumerable<(string Source, string? Model)> Find(
        FastFileWorkspace workspace,
        string animationName)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(animationName);
        if (workspace.IsBlank)
            return [];
        Cache cache = Caches.GetValue(workspace, _ => new Cache());
        lock (cache)
        {
            Refresh(workspace, cache);
            cache.References ??= Index(cache.Sources);
            return cache.References.TryGetValue(XAssetStableIdentity.NormalizeLookupName(animationName), out var references)
                ? references
                : [];
        }
    }

    // Shared with the map/destructible lookup: source payloads are decoded only
    // once for each active asset-pool revision, even when many XAnims are opened.
    internal static IReadOnlyList<(string Name, string Text)> GetSources(FastFileWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.IsBlank)
            return [];
        Cache cache = Caches.GetValue(workspace, _ => new Cache());
        lock (cache)
        {
            Refresh(workspace, cache);
            return cache.Sources;
        }
    }

    private static void Refresh(FastFileWorkspace workspace, Cache cache)
    {
        XAssetPool pool = workspace.LoadedZone.Context.AssetPool;
        long revision = pool.Revision;
        if (cache.Revision == revision)
            return;

        var sources = new List<(string Name, string Text)>();
        foreach (XAssetSlot slot in pool.Slots)
        {
            if (slot.AssetType != XAssetType.RawFile || !IsRelevant(slot.Name) ||
                slot.ActiveProvider.IsReferencePlaceholder ||
                slot.ActiveProvider.Asset is not RawFileAsset rawFile)
                continue;

            try
            {
                byte[] bytes = RawFileContentCodec.DecodeStrictSerializedContent(slot.Name, rawFile);
                RawFileTextEncoding? encoding = RawFileContentClassifier.Classify(slot.Name, bytes).TextEncoding;
                if (encoding is { } textEncoding)
                    sources.Add((slot.Name, RawFileContentClassifier.DecodeText(bytes, textEncoding)));
            }
            catch (InvalidDataException)
            {
                // A damaged RawFile cannot establish a reference.
            }
        }

        if (pool.Revision != revision)
            throw new InvalidOperationException("The loaded asset pool changed during XAnim reference capture.");

        cache.Sources = sources.AsReadOnly();
        cache.References = null;
        cache.Revision = revision;
    }

    private static bool IsRelevant(string name) =>
        name.EndsWith(".gsc", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase) ||
        name.Replace('\\', '/').Equals("mp/playeranim.script", StringComparison.OrdinalIgnoreCase) ||
        (name.Replace('\\', '/').StartsWith("animtrees/", StringComparison.OrdinalIgnoreCase) &&
         name.EndsWith(".atr", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, IReadOnlyList<(string Source, string? Model)>> Index(
        IReadOnlyList<(string Name, string Text)> sources)
    {
        var found = new Dictionary<string, HashSet<(string Source, string? Model)>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string text) in sources)
        {
            if (name.EndsWith(".atr", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string animation in GscAnimationTreeReferences.FindPrimitiveNames(text))
                    Add(animation, name, null);
            }
            else if (name.EndsWith(".script", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string animation in PlayerAnimCommands(text))
                    Add(animation, name, null);
            }
            else
            {
                if (!text.Contains('%') &&
                    !text.Contains("get_precached_anim", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("precacheMpAnim", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("scriptmodelplayanim", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var (animation, model) in GscAnimationReferences.Find(text))
                    Add(animation, name, model);
            }
        }

        return found.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<(string Source, string? Model)>)pair.Value
                .OrderBy(value => value.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Model, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        void Add(string animation, string source, string? model)
        {
            string key = XAssetStableIdentity.NormalizeLookupName(animation);
            if (!found.TryGetValue(key, out var references))
                found.Add(key, references = []);
            references.Add((source, model));
        }
    }

    private static bool IsAssetName(string value) =>
        value.Length != 0 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '/' or '\\' or '-' or '.');

    private static IEnumerable<string> PlayerAnimCommands(string text)
    {
        foreach (string line in ContentLines(text))
        {
            int separator = line.IndexOfAny([' ', '\t']);
            if (separator < 0)
                continue;
            string command = line[..separator];
            if (!command.Equals("both", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("legs", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("torso", StringComparison.OrdinalIgnoreCase) &&
                !command.Equals("turret", StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = line[(separator + 1)..].TrimStart();
            int end = rest.IndexOfAny([' ', '\t', '\r']);
            string animation = end < 0 ? rest : rest[..end];
            if (IsAssetName(animation))
                yield return animation;
        }
    }

    private static IReadOnlyList<string> ContentLines(string text)
    {
        var lines = new List<string>();
        bool blockComment = false;
        foreach (string line in text.Split('\n'))
        {
            var cleaned = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (blockComment)
                {
                    if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        blockComment = false;
                        i++;
                    }
                }
                else if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    blockComment = true;
                    i++;
                }
                else if (line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/')
                    break;
                else
                    cleaned.Append(line[i]);
            }
            lines.Add(cleaned.ToString().Trim());
        }
        return lines;
    }

    private sealed class Cache
    {
        internal long Revision = -1;
        internal IReadOnlyList<(string Name, string Text)> Sources = [];
        internal Dictionary<string, IReadOnlyList<(string Source, string? Model)>>? References;
    }
}
