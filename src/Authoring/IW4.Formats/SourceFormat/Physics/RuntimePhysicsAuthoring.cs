using System.Globalization;
using System.Numerics;
using IW4.Game.Assets.ColMap;
using IW4.Game.Assets.XModel;
using ModelVec3 = IW4.Game.Math.Vec3;

namespace IW4.Formats.SourceFormat.Physics;

public static class RuntimePhysicsAuthoring
{
    public const string ClassName = "dyn_model";
    public const string ModelName = "soccer_ball";
    public const string PresetName = "soccer_ball";

    public static void Validate(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        string? classname = properties.GetValueOrDefault("classname");
        if (classname is not (ClassName or "misc_model"))
            throw new NotSupportedException("Runtime physics conversion requires a misc_model or dyn_model entity.");
        if (properties.GetValueOrDefault("model") != ModelName)
            throw new NotSupportedException($"Runtime physics currently supports only the '{ModelName}' XModel.");

        foreach (string key in properties.Keys)
        {
            if (key is not ("classname" or "model" or "origin" or "angles" or "angle" or
                "modelscale" or "modelscale_vec" or "spawnflags" or "gndLt"))
                throw new NotSupportedException($"Runtime physics model property '{key}' is not supported.");
        }

        _ = ReadVector(properties.GetValueOrDefault("origin") ??
            throw new InvalidDataException("Runtime physics model requires an origin."), "origin");
        _ = ReadAngles(properties);
        if (properties.TryGetValue("modelscale", out string? scale) && ReadNumber(scale, "modelscale") != 1 ||
            properties.TryGetValue("modelscale_vec", out string? scaleVector) &&
            ReadVector(scaleVector, "modelscale_vec") != Vector3.One)
            throw new NotSupportedException("Runtime physics model requires unit scale.");
        if (properties.TryGetValue("spawnflags", out string? flags) &&
            (!int.TryParse(flags, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
             value is not (0 or 2)))
            throw new NotSupportedException("Runtime physics model supports only inherited spawnflags 0 or 2.");
    }

    public static DynEntityDef CreateDefinition(
        IReadOnlyDictionary<string, string> properties,
        XModelAsset model)
    {
        Validate(properties);
        if (properties.GetValueOrDefault("classname") != ClassName)
            throw new InvalidDataException($"Only '{ClassName}' entities compile as runtime physics definitions.");
        ArgumentNullException.ThrowIfNull(model);
        if (model.Name != ModelName || model.PhysPreset?.Name != PresetName || model.PhysCollmap is null)
            throw new InvalidDataException(
                $"Runtime physics XModel '{ModelName}' requires its native '{PresetName}' PhysPreset and PhysCollmap.");

        Vector3 origin = ReadVector(properties["origin"], "origin");
        Vector3 angles = ReadAngles(properties);
        double pitch = angles.X * (Math.PI / 180), yaw = angles.Y * (Math.PI / 180),
            roll = angles.Z * (Math.PI / 180);
        (double sp, double cp) = Math.SinCos(pitch);
        (double sy, double cy) = Math.SinCos(yaw);
        (double sr, double cr) = Math.SinCos(roll);
        var rotation = new Matrix4x4(
            (float)(cp * cy), (float)(cp * sy), (float)-sp, 0,
            (float)(sr * sp * cy - cr * sy), (float)(sr * sp * sy + cr * cy), (float)(sr * cp), 0,
            (float)(cr * sp * cy + sr * sy), (float)(cr * sp * sy - sr * cy), (float)(cr * cp), 0,
            0, 0, 0, 1);
        Quaternion quaternion = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));

        // PS3 mp_favela.ff DynEntDefList[0][453]: CLUTTER, zero mass/brush fields,
        // contents 1, soccer_ball XModel/PhysPreset; retain that XModel's native collmap.
        return new DynEntityDef
        {
            Type = 1,
            Pose = new GfxPlacement
            {
                Quat = [quaternion.X, quaternion.Y, quaternion.Z, quaternion.W],
                Origin = new ModelVec3 { X = origin.X, Y = origin.Y, Z = origin.Z }
            },
            XModel = model,
            PhysPreset = model.PhysPreset,
            Contents = 1
        };
    }

    private static Vector3 ReadAngles(IReadOnlyDictionary<string, string> properties)
    {
        if (properties.TryGetValue("angles", out string? text))
        {
            Vector3 angles = ReadVector(text, "angles");
            if (properties.ContainsKey("angle"))
                throw new InvalidDataException("Runtime physics model cannot specify both angle and angles.");
            return angles;
        }
        if (!properties.TryGetValue("angle", out text)) return Vector3.Zero;
        float yaw = ReadNumber(text, "angle");
        return yaw == -1 ? new Vector3(-90, 0, 0) : yaw == -2
            ? new Vector3(90, 0, 0) : new Vector3(0, yaw, 0);
    }

    private static Vector3 ReadVector(string text, string field)
    {
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            throw new InvalidDataException($"Runtime physics model {field} requires three numbers.");
        return new Vector3(ReadNumber(parts[0], field), ReadNumber(parts[1], field), ReadNumber(parts[2], field));
    }

    private static float ReadNumber(string text, string field) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? value : throw new InvalidDataException($"Runtime physics model {field} requires finite numbers.");
}
