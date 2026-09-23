namespace IW4.Render.Scheduling.Dpvs;

/// <summary>
/// Commands emitted by one exact DPVS traversal producer. A camera command
/// set records the resolver cell used to begin portal traversal.
/// </summary>
public sealed class MapRenderWorldDpvsViewCommandSet
{
    private readonly MapRenderWorldDpvsCellCullCommandData[] _commandData;
    private IReadOnlyList<MapRenderWorldDpvsCellCullCommand>? _commands;

    public MapRenderWorldDpvsViewCommandSet(
        MapRenderWorldDpvsViewIndex viewIndex,
        MapRenderWorldDpvsCommandOrigin origin,
        IReadOnlyList<MapRenderWorldDpvsCellCullCommand> commands,
        int? cameraStartCellIndex = null)
        : this(
            viewIndex,
            origin,
            CopyPublicCommands(commands, out IReadOnlyList<
                MapRenderWorldDpvsCellCullCommand> retainedCommands),
            cameraStartCellIndex)
    {
        _commands = retainedCommands;
    }

    internal MapRenderWorldDpvsViewCommandSet(
        MapRenderWorldDpvsViewIndex viewIndex,
        MapRenderWorldDpvsCommandOrigin origin,
        IReadOnlyList<MapRenderWorldDpvsCellCullCommandData> commands,
        int? cameraStartCellIndex = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ValidateRole(
            viewIndex,
            origin,
            commands.Count,
            cameraStartCellIndex);
        ViewIndex = viewIndex;
        Origin = origin;
        CameraStartCellIndex = cameraStartCellIndex;
        _commandData = commands.ToArray();
    }

    public MapRenderWorldDpvsViewIndex ViewIndex { get; }

    public MapRenderWorldDpvsCommandOrigin Origin { get; }

    public IReadOnlyList<MapRenderWorldDpvsCellCullCommand> Commands
    {
        get
        {
            IReadOnlyList<MapRenderWorldDpvsCellCullCommand>? commands =
                Volatile.Read(ref _commands);
            if (commands is not null)
                return commands;

            var materialized =
                new MapRenderWorldDpvsCellCullCommand[_commandData.Length];
            for (int index = 0; index < materialized.Length; index++)
                materialized[index] = new(_commandData[index]);
            IReadOnlyList<MapRenderWorldDpvsCellCullCommand> immutable =
                Array.AsReadOnly(materialized);
            return Interlocked.CompareExchange(
                    ref _commands,
                    immutable,
                    comparand: null) ??
                immutable;
        }
    }

    public int? CameraStartCellIndex { get; }

    internal ReadOnlySpan<MapRenderWorldDpvsCellCullCommandData> CommandSpan =>
        _commandData;

    private static void ValidateRole(
        MapRenderWorldDpvsViewIndex viewIndex,
        MapRenderWorldDpvsCommandOrigin origin,
        int commandCount,
        int? cameraStartCellIndex)
    {
        if (!Enum.IsDefined(viewIndex))
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        if (!Enum.IsDefined(origin))
            throw new ArgumentOutOfRangeException(nameof(origin));
        bool isCamera = viewIndex == MapRenderWorldDpvsViewIndex.Camera;
        if (isCamera !=
            (origin == MapRenderWorldDpvsCommandOrigin.CameraPortalTraversal))
        {
            throw new ArgumentException(
                "Camera view zero requires portal provenance; secondary views require sun-frustum provenance.",
                nameof(origin));
        }
        if (isCamera && commandCount == 0)
        {
            throw new ArgumentException(
                "The operational camera portal traversal must emit at least one cell command.",
                "commands");
        }
        if (isCamera)
        {
            if (cameraStartCellIndex is null or < -1)
                throw new ArgumentOutOfRangeException(nameof(cameraStartCellIndex));
        }
        else if (cameraStartCellIndex is not null)
        {
            throw new ArgumentException(
                "A secondary frustum traversal has no camera portal start cell.",
                nameof(cameraStartCellIndex));
        }
    }

    private static MapRenderWorldDpvsCellCullCommandData[]
        CopyPublicCommands(
            IReadOnlyList<MapRenderWorldDpvsCellCullCommand> commands,
            out IReadOnlyList<MapRenderWorldDpvsCellCullCommand>
                retainedCommands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var copied =
            new MapRenderWorldDpvsCellCullCommand[commands.Count];
        var data =
            new MapRenderWorldDpvsCellCullCommandData[commands.Count];
        for (int index = 0; index < commands.Count; index++)
        {
            MapRenderWorldDpvsCellCullCommand command =
                commands[index] ??
                throw new ArgumentException(
                    "A DPVS command set cannot contain null commands.",
                    nameof(commands));
            copied[index] = command;
            data[index] = command.Data;
        }
        retainedCommands = Array.AsReadOnly(copied);
        return data;
    }
}
