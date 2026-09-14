using System.Globalization;
using System.Numerics;
using Iw4Radiant.MapSource;

namespace Iw4Radiant.Editing;

internal static class EntityOrientation
{
    internal static Vector3 Forward(Vector3 angles)
    {
        var (sp, cp) = SinCos(angles.X);
        var (sy, cy) = SinCos(angles.Y);
        return new Vector3((float)(cp * cy), (float)(cp * sy), (float)-sp);
    }

    internal static Matrix4x4 Rotation(MapEntity entity)
    {
        var (forward, side, up) = Axes(Read(entity));
        return new Matrix4x4((float)forward.X, (float)forward.Y, (float)forward.Z, 0,
            (float)side.X, (float)side.Y, (float)side.Z, 0,
            (float)up.X, (float)up.Y, (float)up.Z, 0, 0, 0, 0, 1);
    }

    internal static void Transform(MapEntity entity, Matrix4x4 transform)
    {
        if (!Matrix4x4.Decompose(transform, out _, out Quaternion rotation, out _) ||
            rotation.X == 0 && rotation.Y == 0 && rotation.Z == 0) return;
        Vector3 angles = Read(entity);
        // Radiant uses pitch/yaw/roll with Z up and pitch increasing toward -Z.
        // Compose in double precision so tiny rotations and near-pole components
        // survive until the resulting map angles are rounded to their float format.
        var axes = Axes(angles);
        var forward = Rotate(axes.Forward, rotation);
        var side = Rotate(axes.Side, rotation);
        var up = Rotate(axes.Up, rotation);
        const double degrees = 180 / Math.PI;
        double horizontal = Math.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
        float pitch = (float)(Math.Atan2(-forward.Z, horizontal) * degrees);
        bool pole = pitch is 90 or -90;
        // At a pitch pole, only the coupled yaw/roll is observable. Choose roll
        // zero and recover that orientation from the stable side vector. A pitch
        // already rounded to +/-90 cannot encode any remaining sub-ULP tilt.
        float yaw = (float)((pole ? Math.Atan2(-side.X, side.Y) : Math.Atan2(forward.Y, forward.X)) * degrees);
        float roll = pole ? 0 : (float)(Math.Atan2(side.Z, up.Z) * degrees);
        if (entity.Properties.ContainsKey("angle") && !entity.Properties.ContainsKey("angles") &&
            pitch == 0 && roll == 0 && yaw is not (-1 or -2))
            entity.Properties["angle"] = yaw.ToString("G9", CultureInfo.InvariantCulture);
        else
        {
            entity.Properties["angles"] = FormattableString.Invariant($"{pitch:G9} {yaw:G9} {roll:G9}");
            entity.Properties.Remove("angle");
        }
    }

    private static ((double X, double Y, double Z) Forward, (double X, double Y, double Z) Side,
        (double X, double Y, double Z) Up) Axes(Vector3 angles)
    {
        var (sp, cp) = SinCos(angles.X);
        var (sy, cy) = SinCos(angles.Y);
        var (sr, cr) = SinCos(angles.Z);
        return ((cp * cy, cp * sy, -sp),
            (sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp),
            (cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp));
    }

    private static (double Sin, double Cos) SinCos(float angle) => (angle % 360) switch
    {
        0 => (0, 1), 90 or -270 => (1, 0), 180 or -180 => (0, -1), 270 or -90 => (-1, 0),
        var degrees => Math.SinCos(degrees * (Math.PI / 180))
    };

    private static (double X, double Y, double Z) Rotate((double X, double Y, double Z) value, Quaternion rotation)
    {
        double x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;
        double inverseLength = 1 / Math.Sqrt(x * x + y * y + z * z + w * w);
        x *= inverseLength; y *= inverseLength; z *= inverseLength; w *= inverseLength;
        double tx = 2 * (y * value.Z - z * value.Y), ty = 2 * (z * value.X - x * value.Z),
            tz = 2 * (x * value.Y - y * value.X);
        return (value.X + w * tx + y * tz - z * ty, value.Y + w * ty + z * tx - x * tz,
            value.Z + w * tz + x * ty - y * tx);
    }

    internal static Vector3 Read(MapEntity entity)
    {
        if (entity.Properties.TryGetValue("angles", out string? text))
        {
            string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new ArgumentException("Entity angles must contain pitch, yaw and roll.");
            return new Vector3(Number(parts[0]), Number(parts[1]), Number(parts[2]));
        }
        if (!entity.Properties.TryGetValue("angle", out text)) return Vector3.Zero;
        float yaw = Number(text);
        return yaw == -1 ? new Vector3(-90, 0, 0) : yaw == -2 ? new Vector3(90, 0, 0) : new Vector3(0, yaw, 0);
    }

    private static float Number(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
        ? value : throw new ArgumentException("Entity angles must be finite numbers.");
}
