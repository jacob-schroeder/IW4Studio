using System.Runtime.CompilerServices;
using IW4.Game.Assets;
using IW4.Loaders.Database;
using IW4.Linker.Contracts;
using IW4.Linker.Plans;

namespace IW4.Linker.Capture;

/// <summary>Turns retained loader facts into one symbolic import graph.</summary>
public static class ZoneObjectObservationTranslator
{
    private static readonly ConditionalWeakTable<ZoneObjectObservation, ZoneObjectImport> Imports = new();

    public static ZoneObjectImport Translate(ZoneObjectObservation observation) =>
        Imports.GetValue(observation, static source => Replay(source));

    private static ZoneObjectImport Replay(ZoneObjectObservation observation)
    {
        var capture = new ZoneObjectCapture(observation.DecodedTape.Span, observation.Layout);
        var occurrences = new Dictionary<long, CaptureOccurrence>();
        var providerOccurrences = new Dictionary<BaseAsset, CaptureOccurrence>(ReferenceEqualityComparer.Instance);
        foreach (ZoneObjectObservationEvent @event in observation.Events)
        {
            switch (@event)
            {
                case TempEpochEntered entered:
                    if (capture.EnterTempEpoch() != entered.Epoch)
                        throw new InvalidDataException("TEMP observation epoch differs from its replayed lifetime.");
                    break;
                case TempEpochRetired retired:
                    capture.RetireTempEpoch(retired.Epoch);
                    break;
                case ZoneMaterialized materialized:
                    occurrences.Add(materialized.Id, capture.RecordMaterialization(
                        materialized.DecodedOffset, materialized.Length, materialized.Destination,
                        materialized.Alignment, TranslateKind(materialized.Kind), materialized.TempEpoch));
                    break;
                case ZonePointerRead pointer:
                    int? tapeOffset = pointer.DecodedOffset ??
                        (pointer.Cell is { } pointerCell
                            ? capture.FindTapeOffset(pointerCell, pointer.CellTempEpoch)
                            : null);
                    occurrences.Add(pointer.Id, capture.RecordPointer(
                        tapeOffset, pointer.Cell, pointer.Raw, pointer.ResolutionMode,
                        pointer.TemporalEpoch, pointer.CellTempEpoch));
                    break;
                case ZoneInsertCellStaged cell:
                    capture.RecordInsertPointerCell(cell.Cell, cell.TempEpoch);
                    break;
                case ZoneInlineTargetBound binding:
                    capture.BindInlineTarget(occurrences[binding.PointerId], binding.Target,
                        binding.Alignment, binding.TargetTempEpoch);
                    break;
                case ZoneValidatedTargetBound binding:
                    capture.BindValidatedTarget(occurrences[binding.PointerId], binding.Target,
                        binding.Length, binding.TargetTempEpoch);
                    break;
                case ZoneXStringMarked text:
                    capture.MarkXString(occurrences[text.MaterializationId]);
                    break;
                case ZoneProviderRegistered provider:
                    CaptureOccurrence occurrence = capture.RecordProviderRegistration(
                        occurrences[provider.PointerId], provider.Materialization,
                        provider.IncomingIdentity, provider.ActiveIdentity, provider.InsertCell);
                    if (!providerOccurrences.TryAdd(provider.Provider, occurrence))
                        throw new InvalidDataException("One provider object was registered by more than one captured source occurrence.");
                    break;
                default:
                    throw new InvalidDataException("Unknown zone-object observation.");
            }
        }

        ZoneObjectFile objectFile = capture.Freeze();
        return new ZoneObjectImport(objectFile,
            new ZoneObjectLinkImportResolver(objectFile, providerOccurrences));
    }

    private static MaterializationKind TranslateKind(ZoneMaterializationKind kind) => kind switch
    {
        ZoneMaterializationKind.StreamCopy => MaterializationKind.StreamCopy,
        ZoneMaterializationKind.CString => MaterializationKind.CString,
        ZoneMaterializationKind.RuntimeZeroFill => MaterializationKind.RuntimeZeroFill,
        ZoneMaterializationKind.VirtualReservation => MaterializationKind.VirtualReservation,
        ZoneMaterializationKind.InsertCell => MaterializationKind.InsertCell,
        _ => throw new InvalidDataException("Unknown zone materialization kind.")
    };
}

public sealed class ZoneObjectImport
{
    internal ZoneObjectImport(ZoneObjectFile objectFile, ZoneObjectLinkImportResolver resolver)
    {
        ObjectFile = objectFile;
        Resolver = resolver;
    }

    public ZoneObjectFile ObjectFile { get; }
    public ILinkAssetImportResolver Resolver { get; }
}
