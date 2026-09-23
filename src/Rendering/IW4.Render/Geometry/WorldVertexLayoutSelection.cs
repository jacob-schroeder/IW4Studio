using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.GfxMap;
using IW4.Game.Assets.Image;
using IW4.Game.Assets.Material;
using IW4.Game.Assets.TechniqueSet;
using IW4.Game.Assets.XModel;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Render.Geometry;

internal readonly record struct WorldVertexLayoutSelection(
    MaterialWorldVertexFormat? LogicalFormat,
    int BackendRow,
    string Label)
{
    public bool IsResolved =>
        LogicalFormat.HasValue && WorldVertexLayout.HasBackendRow(BackendRow);

    public string FormatText => LogicalFormat.HasValue
        ? BackendRow >= 0
            ? $"{LogicalFormat.Value}/backendRow{BackendRow}"
            : LogicalFormat.Value.ToString()
        : string.Empty;

    public static WorldVertexLayoutSelection Unresolved(
        MaterialWorldVertexFormat? logicalFormat) =>
        new(logicalFormat, -1, "unresolved");
}
