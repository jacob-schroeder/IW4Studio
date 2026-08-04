using IW4.Assets.Assets.Sound;
using IW4.FastFiles.Pointers;
using IW4.FastFiles.Zone;
using IW4.FastFiles.Emitters.Emission;

namespace IW4.FastFiles.Emitters.Assets;

/// <summary>
/// Emitter for the language-counted SoundFile array in each SndAlias. Imported
/// nested LoadedSound and SndCurve pointers retain ownership provenance.
/// Exact imported linking may also retain dependency-owned packed direct
/// SoundFile/SpeakerMap values; canonical linking requires a local provider.
/// </summary>
public sealed class SoundAliasListBodyEmitter : IXAssetBodyEmitter
{
    public XAssetType AssetType => XAssetType.Sound;

    public IReadOnlyList<EmissionError> Validate(
        IXAssetBuildData buildData,
        int? rowIndex = null)
    {
        var errors = AssetBodyEmitterHelpers.ValidateIdentity(
            buildData,
            AssetType,
            rowIndex);
        if (buildData is not ISoundAliasListBuildData data)
        {
            errors.Add(Error(
                "body",
                "Sound build data does not implement ISoundAliasListBuildData.",
                rowIndex));
            return errors;
        }

        CheckString(data.AliasName, "aliasName", errors, rowIndex);
        for (int index = 0; index < data.Aliases.Count; index++)
            CheckAlias(data.Aliases[index], index, errors, rowIndex);
        if (data.Aliases
                .Select(alias => alias.SoundFiles.Count)
                .Where(count => count != 0)
                .Distinct()
                .Take(2)
                .Count() > 1)
        {
            errors.Add(Error(
                "aliases",
                "Every non-empty SoundFile array must use the same language count.",
                rowIndex));
        }
        return errors;
    }

    public AssetBodyEmission Plan(
        IXAssetBuildData buildData,
        EmissionPlan plan,
        int? rowIndex = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AssetBodyEmitterHelpers.RequireNoDiagnostics(
            Validate(buildData, rowIndex));
        ISoundAliasListBuildData data = (ISoundAliasListBuildData)buildData;
        var all = new List<EmissionBlockSegment>();
        var source = new List<EmissionBlockSegment>();

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(0x0c, 4);
        plan.Push(XFileBlockType.LARGE);
        StringPlan name = PlanString(data.AliasName, plan, all);
        EmissionBlockSegment? aliases = null;
        AliasPlan[] aliasPlans = [];
        if (data.Aliases.Count != 0)
        {
            EmissionAddress address = plan.Allocate(
                checked(data.Aliases.Count * 0x64),
                4);
            aliasPlans = data.Aliases
                .Select((alias, index) => PlanAlias(
                    alias,
                    index,
                    new EmissionAddress(
                        address.Block,
                        checked(address.Offset + index * 0x64)),
                    plan,
                    all))
                .ToArray();
            aliases = new EmissionBlockSegment(
                address,
                BuildAliasTable(aliasPlans));
            all.Add(aliases);
        }
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var writer = new XSourceWriter();
        writer.WriteInt32(name.Raw);
        writer.WriteInt32(Pointer(aliases));
        writer.WriteInt32(data.Aliases.Count);
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray());
        all.Add(rootSegment);
        source.Add(rootSegment);
        source.AddRange(name.Source);
        Add(source, aliases);
        foreach (AliasPlan alias in aliasPlans)
            source.AddRange(alias.Source);
        return new AssetBodyEmission(AssetType, root, all, source);
    }

    private static AliasPlan PlanAlias(
        SoundAliasBuildData data,
        int aliasIndex,
        EmissionAddress aliasRoot,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        var source = new List<EmissionBlockSegment>();
        StringPlan aliasName = PlanString(data.AliasName, plan, all);
        StringPlan subtitle = PlanString(data.Subtitle, plan, all);
        StringPlan secondary = PlanString(data.SecondaryAliasName, plan, all);
        StringPlan chain = PlanString(data.ChainAliasName, plan, all);
        StringPlan mixer = PlanString(data.MixerGroup, plan, all);
        source.AddRange(aliasName.Source);
        source.AddRange(subtitle.Source);
        source.AddRange(secondary.Source);
        source.AddRange(chain.Source);
        source.AddRange(mixer.Source);

        SoundFileArrayPlan soundFiles = PlanSoundFiles(
            data,
            aliasIndex,
            plan,
            all);
        source.AddRange(soundFiles.Source);

        int curveRaw = 0;
        EmissionAddress curveOwnerCell = new(
            aliasRoot.Block,
            checked(aliasRoot.Offset + 0x50));
        if (data.VolumeFalloffCurveLink is { } curveLink)
        {
            NestedXAssetPlan curve = NestedXAssetEmission.Plan(
                curveLink,
                plan,
                all,
                curveOwnerCell,
                owner: "Sound.VolumeFalloffCurve");
            curveRaw = curve.PointerRaw;
            source.AddRange(curve.Source);
        }
        else if (data.VolumeFalloffCurveReference is { } curveReference)
        {
            AssetBodyEmission curve = PlanExternal(
                curveReference,
                XAssetType.SndCurve,
                0x88,
                plan,
                all);
            curveRaw = -1;
            source.AddRange(curve.SourceSegments);
        }

        DirectPointerPlan speakerPointer = PlanDirectPointer(
            data.SpeakerMapPointerProvenance,
            data.SpeakerMap is not null,
            plan,
            $"aliases[{aliasIndex}].speakerMap");
        int speakerRaw = speakerPointer.Raw;
        if (speakerPointer.EmitPayload &&
            data.SpeakerMap is { } speaker)
        {
            if (speakerPointer.SourceForm ==
                SoundDirectPointerSourceForm.Insert)
            {
                plan.AllocateInsertPointerCell(
                    "Sound",
                    $"aliases[{aliasIndex}].speakerMap.insert");
            }

            AssetBodyEmission speakerPlan = PlanSpeakerMap(
                speaker,
                plan,
                all);
            RequirePackedPayloadAtCurrentAddress(
                speakerPointer,
                speakerPlan.RootAddress,
                $"aliases[{aliasIndex}].speakerMap");
            source.AddRange(speakerPlan.SourceSegments);
        }

        return new AliasPlan(
            data,
            aliasName.Raw,
            subtitle.Raw,
            secondary.Raw,
            chain.Raw,
            mixer.Raw,
            soundFiles.Raw,
            curveRaw,
            speakerRaw,
            source);
    }

    private static SoundFileArrayPlan PlanSoundFiles(
        SoundAliasBuildData data,
        int aliasIndex,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        string path = $"aliases[{aliasIndex}].soundFiles";
        DirectPointerPlan pointer = PlanDirectPointer(
            data.SoundFilesPointerProvenance,
            data.SoundFiles.Count != 0,
            plan,
            path);
        if (!pointer.EmitPayload)
            return new SoundFileArrayPlan(pointer.Raw, []);

        if (pointer.SourceForm == SoundDirectPointerSourceForm.Insert)
        {
            plan.AllocateInsertPointerCell(
                "Sound",
                $"{path}.insert");
        }

        plan.Push(XFileBlockType.TEMP);
        EmissionAddress address = plan.Allocate(
            checked(data.SoundFiles.Count * SoundFile.SerializedSize),
            4);
        RequirePackedPayloadAtCurrentAddress(pointer, address, path);
        plan.Push(XFileBlockType.LARGE);

        var childSource = new List<EmissionBlockSegment>();
        var writer = new XSourceWriter();
        for (int index = 0; index < data.SoundFiles.Count; index++)
        {
            SoundFileBuildData soundFile = data.SoundFiles[index];
            int unionRaw0;
            int unionRaw1;
            int unionRaw2;
            if (soundFile.Kind == SndAliasTypeBuildKind.Loaded)
            {
                EmissionAddress ownerCell = new(
                    address.Block,
                    checked(address.Offset +
                            index * SoundFile.SerializedSize +
                            sizeof(int)));
                if (soundFile.LoadedSoundLink is { } link)
                {
                    NestedXAssetPlan child = NestedXAssetEmission.Plan(
                        link,
                        plan,
                        all,
                        ownerCell,
                        owner: "Sound.LoadedSound");
                    unionRaw0 = child.PointerRaw;
                    childSource.AddRange(child.Source);
                }
                else if (soundFile.LoadedSoundReference is { } loaded)
                {
                    AssetBodyEmission child = PlanExternal(
                        loaded,
                        XAssetType.LoadedSound,
                        0x1c,
                        plan,
                        all);
                    unionRaw0 = -1;
                    childSource.AddRange(child.SourceSegments);
                }
                else
                {
                    unionRaw0 = 0;
                }
                unionRaw1 = 0;
                unionRaw2 = 0;
            }
            else
            {
                unionRaw0 = unchecked((int)soundFile.StreamedFileIndex);
                if (soundFile.StreamedFileIndex == 0)
                {
                    StringPlan directory = PlanString(
                        soundFile.ExternalDirectory,
                        plan,
                        all);
                    StringPlan filename = PlanString(
                        soundFile.ExternalFilename,
                        plan,
                        all);
                    unionRaw1 = directory.Raw;
                    unionRaw2 = filename.Raw;
                    childSource.AddRange(directory.Source);
                    childSource.AddRange(filename.Source);
                }
                else
                {
                    unionRaw1 = soundFile.StreamFileOffset;
                    unionRaw2 = soundFile.StreamFileLength;
                }
            }

            writer.WriteByte((byte)soundFile.Kind);
            writer.WriteByte(soundFile.Exists);
            writer.WriteUInt16(soundFile.Padding);
            writer.WriteInt32(unionRaw0);
            writer.WriteInt32(unionRaw1);
            writer.WriteInt32(unionRaw2);
        }

        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);

        var roots = new EmissionBlockSegment(address, writer.ToArray());
        all.Add(roots);
        return new SoundFileArrayPlan(
            pointer.Raw,
            [roots, .. childSource]);
    }

    private static DirectPointerPlan PlanDirectPointer(
        SoundDirectPointerBuildProvenance? provenance,
        bool hasPayload,
        EmissionPlan plan,
        string path)
    {
        SoundDirectPointerSourceForm sourceForm =
            provenance?.SourceForm ??
            (hasPayload
                ? SoundDirectPointerSourceForm.Inline
                : SoundDirectPointerSourceForm.Null);

        return sourceForm switch
        {
            SoundDirectPointerSourceForm.Null =>
                new(0, false, sourceForm),
            SoundDirectPointerSourceForm.Inline =>
                new(-1, true, sourceForm),
            SoundDirectPointerSourceForm.Insert =>
                new(-2, true, sourceForm),
            SoundDirectPointerSourceForm.PackedAlias
                when plan.PreserveImportedXAssetPointerValues =>
                new(
                    provenance?.ImportedPackedRaw ??
                    throw new InvalidDataException(
                        $"{path} packed pointer has no imported raw value."),
                    hasPayload,
                    sourceForm),
            SoundDirectPointerSourceForm.PackedAlias when hasPayload =>
                new(-1, true, SoundDirectPointerSourceForm.Inline),
            SoundDirectPointerSourceForm.PackedAlias =>
                throw new InvalidDataException(
                    $"{path} is a dependency-owned packed pointer with no " +
                    "detached payload; canonical linking requires a provider."),
            _ => throw new InvalidDataException(
                $"{path} has unsupported direct pointer source form {sourceForm}.")
        };
    }

    private static void RequirePackedPayloadAtCurrentAddress(
        DirectPointerPlan pointer,
        EmissionAddress payloadAddress,
        string path)
    {
        if (pointer.SourceForm != SoundDirectPointerSourceForm.PackedAlias)
            return;

        int relocatedRaw = payloadAddress.ToPackedPointer();
        if (pointer.Raw != relocatedRaw)
        {
            throw new InvalidDataException(
                $"{path} retained packed pointer " +
                $"0x{unchecked((uint)pointer.Raw):X8}, but its inline source " +
                $"materialized at 0x{unchecked((uint)relocatedRaw):X8}. " +
                "A packed Sound pointer consumes source only when it equals " +
                "the current destination cursor.");
        }
    }

    private static byte[] BuildAliasTable(IReadOnlyList<AliasPlan> values)
    {
        var writer = new XSourceWriter();
        foreach (AliasPlan plan in values)
        {
            SoundAliasBuildData value = plan.Data;
            writer.WriteInt32(plan.AliasNameRaw);
            writer.WriteInt32(plan.SubtitleRaw);
            writer.WriteInt32(plan.SecondaryRaw);
            writer.WriteInt32(plan.ChainRaw);
            writer.WriteInt32(plan.MixerRaw);
            writer.WriteInt32(plan.SoundFileRaw);
            writer.WriteInt32(value.Sequence);
            writer.WriteSingle(value.VolumeMin);
            writer.WriteSingle(value.VolumeMax);
            writer.WriteSingle(value.PitchMin);
            writer.WriteSingle(value.PitchMax);
            writer.WriteSingle(value.DistanceMin);
            writer.WriteSingle(value.DistanceMax);
            writer.WriteSingle(value.VelocityMin);
            writer.WriteInt32(value.Flags);
            writer.WriteSingle(value.SlavePercentage);
            writer.WriteSingle(value.Probability);
            writer.WriteSingle(value.LfePercentage);
            writer.WriteSingle(value.CenterPercentage);
            writer.WriteInt32(value.StartDelay);
            writer.WriteInt32(plan.CurveRaw);
            writer.WriteSingle(value.EnvelopMin);
            writer.WriteSingle(value.EnvelopMax);
            writer.WriteSingle(value.EnvelopPercentage);
            writer.WriteInt32(plan.SpeakerRaw);
        }
        return writer.ToArray();
    }

    private static AssetBodyEmission PlanSpeakerMap(
        SoundSpeakerMapBuildData data,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        EmissionAddress root = plan.Allocate(0x198, 4);
        StringPlan name = PlanString(data.Name, plan, all);
        var writer = new XSourceWriter();
        writer.WriteByte(data.IsDefault);
        writer.WriteBytes(data.Padding.ToArray());
        writer.WriteInt32(name.Raw);
        foreach (SoundChannelMapBuildData channel in data.ChannelMaps)
        {
            writer.WriteInt32(channel.EntryCount);
            foreach (SoundSpeakerLevelBuildData speaker in channel.Speakers)
            {
                writer.WriteInt32(speaker.Speaker);
                writer.WriteInt32(speaker.NumLevels);
                writer.WriteSingle(speaker.Level0);
                writer.WriteSingle(speaker.Level1);
            }
        }
        var rootSegment = new EmissionBlockSegment(root, writer.ToArray());
        all.Add(rootSegment);
        return new AssetBodyEmission(
            XAssetType.Sound,
            root,
            [rootSegment, .. name.Source],
            [rootSegment, .. name.Source]);
    }

    private static AssetBodyEmission PlanExternal(
        SymbolicXAssetReference reference,
        XAssetType type,
        int size,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        plan.Push(XFileBlockType.TEMP);
        EmissionAddress root = plan.Allocate(size, 4);
        plan.Push(XFileBlockType.LARGE);
        StringPlan name = PlanString(
            reference.OriginalSerializedName,
            plan,
            all);
        plan.Pop(XFileBlockType.LARGE);
        plan.Pop(XFileBlockType.TEMP);
        var rootWriter = new XSourceWriter();
        rootWriter.WriteInt32(name.Raw);
        rootWriter.Reserve(size - 4);
        var rootSegment = new EmissionBlockSegment(root, rootWriter.ToArray());
        all.Add(rootSegment);
        return new AssetBodyEmission(
            type,
            root,
            [rootSegment, .. name.Source],
            [rootSegment, .. name.Source]);
    }

    private static StringPlan PlanString(
        string? value,
        EmissionPlan plan,
        List<EmissionBlockSegment> all)
    {
        int before = all.Count;
        PlannedString? planned = AssetBodyEmitterHelpers.PlanString(
            value,
            plan,
            all,
            plan.StringAliases);
        return new StringPlan(
            AssetBodyEmitterHelpers.SourcePointer(planned),
            all.Skip(before).ToArray());
    }

    private static int Pointer(EmissionBlockSegment? value) =>
        value is null ? 0 : -1;

    private static void Add(
        List<EmissionBlockSegment> source,
        EmissionBlockSegment? value)
    {
        if (value is not null)
            source.Add(value);
    }

    private static void CheckAlias(
        SoundAliasBuildData data,
        int index,
        List<EmissionError> errors,
        int? rowIndex)
    {
        string path = $"aliases[{index}]";
        CheckString(data.AliasName, $"{path}.aliasName", errors, rowIndex);
        CheckString(data.Subtitle, $"{path}.subtitle", errors, rowIndex);
        CheckString(
            data.SecondaryAliasName,
            $"{path}.secondaryAliasName",
            errors,
            rowIndex);
        CheckString(
            data.ChainAliasName,
            $"{path}.chainAliasName",
            errors,
            rowIndex);
        CheckString(data.MixerGroup, $"{path}.mixerGroup", errors, rowIndex);
        foreach (float value in new[]
                 {
                     data.VolumeMin, data.VolumeMax, data.PitchMin,
                     data.PitchMax, data.DistanceMin, data.DistanceMax,
                     data.VelocityMin, data.SlavePercentage,
                     data.Probability, data.LfePercentage,
                     data.CenterPercentage, data.EnvelopMin,
                     data.EnvelopMax, data.EnvelopPercentage
                 })
        {
            if (!float.IsFinite(value))
                errors.Add(Error(path, "Alias ranges must be finite.", rowIndex));
        }
        for (int fileIndex = 0; fileIndex < data.SoundFiles.Count; fileIndex++)
        {
            CheckFile(
                data.SoundFiles[fileIndex],
                $"{path}.soundFiles[{fileIndex}]",
                errors,
                rowIndex);
        }
        CheckDirectPointer(
            data.SoundFilesPointerProvenance,
            data.SoundFiles.Count != 0,
            $"{path}.soundFiles",
            errors,
            rowIndex);
        if (data.VolumeFalloffCurveLink is { } curveLink)
        {
            errors.AddRange(NestedXAssetEmission.Validate(
                curveLink,
                XAssetType.SndCurve,
                $"{path}.volumeFalloffCurve",
                rowIndex,
                XAssetType.Sound));
        }
        else
        {
            CheckReference(
                data.VolumeFalloffCurveReference,
                XAssetType.SndCurve,
                $"{path}.volumeFalloffCurve",
                errors,
                rowIndex);
        }
        if (data.SpeakerMap is { } speaker)
        {
            CheckString(
                speaker.Name,
                $"{path}.speakerMap.name",
                errors,
                rowIndex);
            if (speaker.Padding.Count != 3 ||
                speaker.ChannelMaps.Count != 4 ||
                speaker.ChannelMaps.Any(channel => channel.Speakers.Count != 6))
            {
                errors.Add(Error(
                    $"{path}.speakerMap",
                    "Speaker map requires 3 padding bytes, four channel maps, and six speakers per map.",
                    rowIndex));
            }
            foreach (SoundChannelMapBuildData map in speaker.ChannelMaps)
            {
                foreach (SoundSpeakerLevelBuildData level in map.Speakers)
                {
                    if (!float.IsFinite(level.Level0) ||
                        !float.IsFinite(level.Level1))
                    {
                        errors.Add(Error(
                            $"{path}.speakerMap",
                            "Speaker levels must be finite.",
                            rowIndex));
                    }
                }
            }
        }
        CheckDirectPointer(
            data.SpeakerMapPointerProvenance,
            data.SpeakerMap is not null,
            $"{path}.speakerMap",
            errors,
            rowIndex);
    }

    private static void CheckDirectPointer(
        SoundDirectPointerBuildProvenance? provenance,
        bool hasPayload,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (provenance is null)
            return;

        switch (provenance.SourceForm)
        {
            case SoundDirectPointerSourceForm.Null:
                if (hasPayload)
                {
                    errors.Add(Error(
                        path,
                        "Null pointer provenance cannot carry a payload.",
                        rowIndex));
                }
                if (provenance.ImportedPackedRaw is not null)
                {
                    errors.Add(Error(
                        path,
                        "Null pointer provenance cannot retain a packed raw value.",
                        rowIndex));
                }
                break;

            case SoundDirectPointerSourceForm.Inline:
            case SoundDirectPointerSourceForm.Insert:
                if (!hasPayload)
                {
                    errors.Add(Error(
                        path,
                        $"{provenance.SourceForm} pointer provenance requires a payload.",
                        rowIndex));
                }
                if (provenance.ImportedPackedRaw is not null)
                {
                    errors.Add(Error(
                        path,
                        $"{provenance.SourceForm} pointer provenance cannot retain a packed raw value.",
                        rowIndex));
                }
                break;

            case SoundDirectPointerSourceForm.PackedAlias:
                if (provenance.ImportedPackedRaw is not { } packedRaw ||
                    XPointerCodec.GetType(packedRaw) != PointerType.Offset)
                {
                    errors.Add(Error(
                        path,
                        "Packed pointer provenance requires an exact offset-form raw value.",
                        rowIndex));
                }
                break;

            default:
                errors.Add(Error(
                    path,
                    $"Unsupported pointer provenance {provenance.SourceForm}.",
                    rowIndex));
                break;
        }
    }

    private static void CheckFile(
        SoundFileBuildData value,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (value.Kind == SndAliasTypeBuildKind.Loaded)
        {
            if (value.LoadedSoundLink is { } link)
            {
                errors.AddRange(NestedXAssetEmission.Validate(
                    link,
                    XAssetType.LoadedSound,
                    path,
                    rowIndex,
                    XAssetType.Sound));
            }
            else
            {
                CheckReference(
                    value.LoadedSoundReference,
                    XAssetType.LoadedSound,
                    path,
                    errors,
                    rowIndex);
            }
        }
        else if (value.StreamedFileIndex == 0)
        {
            CheckString(
                value.ExternalDirectory,
                $"{path}.directory",
                errors,
                rowIndex);
            CheckString(
                value.ExternalFilename,
                $"{path}.filename",
                errors,
                rowIndex);
        }
    }

    private static void CheckReference(
        SymbolicXAssetReference? value,
        XAssetType type,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (value is not null &&
            (value.AssetType != type ||
             !value.IsExternalReference ||
             !AssetBodyEmitterHelpers.IsLatin1CString(
                 value.OriginalSerializedName)))
        {
            errors.Add(Error(
                path,
                $"Reference must be a comma-prefixed external {type} identity.",
                rowIndex));
        }
    }

    private static void CheckString(
        string? value,
        string path,
        List<EmissionError> errors,
        int? rowIndex)
    {
        if (value is { } text &&
            !AssetBodyEmitterHelpers.IsLatin1CString(text))
        {
            errors.Add(Error(
                path,
                "Value must be a Latin-1 C string.",
                rowIndex));
        }
    }

    private static EmissionError Error(
        string path,
        string message,
        int? rowIndex) =>
        new(path, message, rowIndex, XAssetType.Sound);

    private sealed record StringPlan(
        int Raw,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record DirectPointerPlan(
        int Raw,
        bool EmitPayload,
        SoundDirectPointerSourceForm SourceForm);

    private sealed record SoundFileArrayPlan(
        int Raw,
        IReadOnlyList<EmissionBlockSegment> Source);

    private sealed record AliasPlan(
        SoundAliasBuildData Data,
        int AliasNameRaw,
        int SubtitleRaw,
        int SecondaryRaw,
        int ChainRaw,
        int MixerRaw,
        int SoundFileRaw,
        int CurveRaw,
        int SpeakerRaw,
        IReadOnlyList<EmissionBlockSegment> Source);
}
