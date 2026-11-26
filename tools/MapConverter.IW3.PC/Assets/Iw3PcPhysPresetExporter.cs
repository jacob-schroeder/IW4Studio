using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MapConverter.IW3.PC.Readers;

namespace MapConverter.IW3.PC.Assets;

internal static class Iw3PcPhysPresetExporter
{
    internal static byte[] ReadInfoString(Iw3PcZone zone, Iw3PcAsset asset)
    {
        int root = asset.Offset;
        float friction = Float(root + 16);
        bool infiniteFriction = friction >= float.MaxValue;
        var text = new StringBuilder("PHYSIC");
        FloatField("mass", Float(root + 8));
        FloatField("bounce", Float(root + 12));
        FloatField("friction", infiniteFriction ? 0 : friction);
        TextField("isFrictionInfinity", infiniteFriction ? "1" : "0");
        FloatField("bulletForceScale", Float(root + 20));
        FloatField("explosiveForceScale", Float(root + 24));
        TextField("sndAliasPrefix", zone.String(root + 28));
        FloatField("piecesSpreadFraction", Float(root + 32));
        FloatField("piecesUpwardVelocity", Float(root + 36));
        TextField("tempDefaultToCylinder", zone.Bytes(root + 40, 1)[0] == 0 ? "0" : "1");
        return Encoding.Latin1.GetBytes(text.ToString());

        float Float(int offset) => BinaryPrimitives.ReadSingleLittleEndian(zone.Bytes(offset, 4));
        void FloatField(string key, float value) => TextField(key, value.ToString("G6", CultureInfo.InvariantCulture));
        void TextField(string key, string value) => text.Append('\\').Append(key).Append('\\').Append(value);
    }
}
