using IW4.FastFiles.Emitters.Assets;
using IW4.FastFiles.Zone;
using IW4.Runtime.Assets;
using IW4.Studio.Documents;

namespace IW4.Studio.Documents.MenuEditing;

/// <summary>
/// Compiles every target-owned Menu and MenuFile from one save capture. One
/// shared clone context and one authority index keep duplicate logical Menu
/// occurrences and their recursive graph nodes coherent across row
/// boundaries.
/// </summary>
internal sealed class MenuCompilationBatch
{
    private readonly IReadOnlyDictionary<TargetZoneRowIdentity, RowResult>
        _rows;

    private MenuCompilationBatch(
        IReadOnlyDictionary<TargetZoneRowIdentity, RowResult> rows)
    {
        _rows = rows;
    }

    public static MenuCompilationBatch Compile(
        FastFileEditingSaveSnapshot save,
        AssetAuthoringAdapterRegistry adapters)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(adapters);

        var rows = new Dictionary<TargetZoneRowIdentity, MutableRow>();
        for (int index = 0; index < save.TargetRows.Count; index++)
        {
            TargetZoneRowSource row = save.TargetRows[index];
            if (row.State != TargetZoneRowSourceState.Definition ||
                row.SerializedType is not (XAssetType.Menu or XAssetType.MenuFile))
            {
                continue;
            }

            rows.Add(row.Identity, CaptureRow(index, row, save, adapters));
        }

        MenuAuthorityOccurrence[] occurrences = rows.Values
            .Where(row => row.BuildData is not null)
            .SelectMany(Occurrences)
            .ToArray();
        MenuAuthorityIndex authorities = MenuAuthorityIndex.Build(occurrences);
        foreach (MenuAuthorityIssue issue in authorities.Issues)
        {
            if (!rows.TryGetValue(issue.RowIdentity, out MutableRow? row))
                continue;
            string path = issue.RegistrationIndex is { } registration
                ? $"menuFile.registrations[{registration}].authority"
                : "menu.authority";
            row.Issues.Add(new AssetValidationIssue(
                path,
                issue.Message,
                AssetValidationSeverity.Error));
        }

        var graph = new MenuGraphClone();
        Dictionary<string, MenuBuildData> compiledAuthorities = authorities.Authorities
            .ToDictionary(
                authority => authority.NormalizedName,
                authority => authority.Owner.Definition!.Copy(graph),
                StringComparer.Ordinal);

        var results = new Dictionary<TargetZoneRowIdentity, RowResult>();
        foreach (MutableRow row in rows.Values.OrderBy(value => value.RowIndex))
        {
            IXAssetBuildData? normalized = row.BuildData switch
            {
                MenuBuildData menu => NormalizeMenu(
                    menu,
                    compiledAuthorities,
                    graph),
                MenuFileBuildData menuFile => NormalizeMenuFile(
                    menuFile,
                    compiledAuthorities,
                    graph),
                null => null,
                _ => throw new InvalidDataException(
                    "Menu compilation batch received a non-Menu build model.")
            };
            results.Add(
                row.Row.Identity,
                new RowResult(
                    normalized,
                    Array.AsReadOnly(row.Issues.ToArray()),
                    row.Failure));
        }

        return new MenuCompilationBatch(results);
    }

    public bool TryGet(
        TargetZoneRowIdentity identity,
        out IXAssetBuildData? buildData,
        out IReadOnlyList<AssetValidationIssue> issues,
        out string? failure)
    {
        if (_rows.TryGetValue(identity, out RowResult? result))
        {
            buildData = result.BuildData;
            issues = result.Issues;
            failure = result.Failure;
            return true;
        }

        buildData = null;
        issues = [];
        failure = null;
        return false;
    }

    private static MutableRow CaptureRow(
        int rowIndex,
        TargetZoneRowSource row,
        FastFileEditingSaveSnapshot save,
        AssetAuthoringAdapterRegistry adapters)
    {
        var issues = new List<AssetValidationIssue>();
        try
        {
            IAssetAuthoringAdapter adapter = adapters.RequireAdapter(
                row.SerializedType);
            object buildData;
            if (save.TryGetDraftObject(row.Identity, out object? capturedDraft) &&
                capturedDraft is not null)
            {
                issues.AddRange(adapter.ValidateDraft(capturedDraft));
                buildData = adapter.ExportBuildData(capturedDraft);
            }
            else
            {
                object authored = adapter.ImportAuthoredSnapshot(row);
                object draft = adapter.CreateDraft(authored);
                issues.AddRange(adapter.ValidateDraft(draft));
                buildData = authored switch
                {
                    MenuAuthoredSnapshot menu => menu.Data,
                    MenuFileAuthoredSnapshot menuFile => menuFile.Data,
                    _ => throw new InvalidDataException(
                        $"Unexpected authored snapshot '{authored.GetType().Name}' in Menu compilation batch.")
                };
            }

            if (buildData is not IXAssetBuildData typed ||
                typed.AssetType != row.SerializedType)
            {
                throw new InvalidDataException(
                    "Menu adapter exported a contradictory build-data type.");
            }

            return new MutableRow(rowIndex, row, typed, issues, null);
        }
        catch (Exception exception) when (exception is
                   InvalidDataException or
                   InvalidOperationException or
                   ArgumentException or
                   OverflowException)
        {
            return new MutableRow(
                rowIndex,
                row,
                null,
                issues,
                exception.Message);
        }
    }

    private static IEnumerable<MenuAuthorityOccurrence> Occurrences(
        MutableRow row)
    {
        switch (row.BuildData)
        {
            case MenuBuildData menu:
            {
                string name = menu.Definition.Window.Name
                    ?? row.Row.OriginalSerializedName
                    ?? throw new InvalidDataException(
                        "A top-level Menu definition has no logical identity.");
                yield return new MenuAuthorityOccurrence(
                    row.Row.Identity,
                    row.RowIndex,
                    -1,
                    null,
                    MenuAuthorityOccurrenceKind.TopLevelDefinition,
                    name,
                    menu,
                    null);
                yield break;
            }

            case MenuFileBuildData menuFile:
                for (int index = 0; index < menuFile.MenuLinks.Count; index++)
                {
                    NestedXAssetBuildLink link = menuFile.MenuLinks[index];
                    MenuBuildData? definition =
                        link.IncomingDefinition as MenuBuildData;
                    yield return new MenuAuthorityOccurrence(
                        row.Row.Identity,
                        row.RowIndex,
                        index,
                        null,
                        definition is null
                            ? MenuAuthorityOccurrenceKind.MenuFileRegistration
                            : MenuAuthorityOccurrenceKind.MenuFileInlineDefinition,
                        link.Reference.OriginalSerializedName,
                        definition,
                        link.SourceForm);
                }
                yield break;
        }
    }

    private static MenuBuildData NormalizeMenu(
        MenuBuildData menu,
        IReadOnlyDictionary<string, MenuBuildData> authorities,
        MenuGraphClone graph)
    {
        string? name = menu.Definition.Window.Name;
        if (!string.IsNullOrWhiteSpace(name) &&
            authorities.TryGetValue(
                XAssetStableIdentity.NormalizeLookupName(name),
                out MenuBuildData? authority))
        {
            return authority;
        }

        return menu.Copy(graph);
    }

    private static MenuFileBuildData NormalizeMenuFile(
        MenuFileBuildData menuFile,
        IReadOnlyDictionary<string, MenuBuildData> authorities,
        MenuGraphClone graph)
    {
        NestedXAssetBuildLink[] links = menuFile.MenuLinks
            .Select(link =>
            {
                if (link.IncomingDefinition is not MenuBuildData incoming)
                    return link with { IncomingDefinition = null };

                string normalized = XAssetStableIdentity.NormalizeLookupName(
                    link.Reference.OriginalSerializedName);
                MenuBuildData definition = authorities.TryGetValue(
                    normalized,
                    out MenuBuildData? authority)
                    ? authority
                    : incoming.Copy(graph);
                return link with { IncomingDefinition = definition };
            })
            .ToArray();
        return MenuFileBuildData.CreateOwned(menuFile.Name, links);
    }

    private sealed class MutableRow
    {
        public MutableRow(
            int rowIndex,
            TargetZoneRowSource row,
            IXAssetBuildData? buildData,
            List<AssetValidationIssue> issues,
            string? failure)
        {
            RowIndex = rowIndex;
            Row = row;
            BuildData = buildData;
            Issues = issues;
            Failure = failure;
        }

        public int RowIndex { get; }
        public TargetZoneRowSource Row { get; }
        public IXAssetBuildData? BuildData { get; }
        public List<AssetValidationIssue> Issues { get; }
        public string? Failure { get; }
    }

    private sealed record RowResult(
        IXAssetBuildData? BuildData,
        IReadOnlyList<AssetValidationIssue> Issues,
        string? Failure);
}
