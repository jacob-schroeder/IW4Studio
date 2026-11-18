namespace IW4.Render.Shaders;

public static partial class RsxCgSourceDecompiler
{
    // A register can hold unrelated values in different lanes over its lifetime.
    // Keep a named definition until a read needs to assemble lanes, or a conditional
    // write needs the previous contents of the register.
    private sealed class FragmentValues(SourceWriter source)
    {
        private readonly Dictionary<(bool Half, int Index), (string Name, string Mask)> _values = [];

        private readonly Dictionary<(bool Half, int Index), int> _defined = [];

        private static int LaneMask(string lanes)
        {
            int mask = 0;
            foreach (char lane in lanes) mask |= 1 << "xyzw".IndexOf(lane);
            return mask;
        }

        public string Read(bool half, int index, string lanes, bool widen = false)
        {
            if (_values.TryGetValue((half, index), out var value))
            {
                if (lanes.All(value.Mask.Contains))
                {
                    if (lanes == value.Mask) return ConvertRead(value.Name);
                    if (value.Mask.Length == 1) return $"{VectorType(half && !widen, lanes.Length)}({value.Name})";
                    string swizzle = string.Concat(lanes.Select(lane => "xyzw"[value.Mask.IndexOf(lane)]));
                    return ConvertRead(value.Name + SwizzleSuffix(swizzle));
                }
                Materialize(half, index);
            }
            int mask = LaneMask(lanes);
            bool needsInitialValue = (_defined.GetValueOrDefault((half, index)) & mask) != mask;
            return ConvertRead(Register(half, index, needsInitialValue) + SwizzleSuffix(lanes));

            string ConvertRead(string expression) => widen ? $"{VectorType(false, lanes.Length)}({expression})" : expression;
        }

        public void BeforeWrite(bool half, int index, string mask, bool conditional)
        {
            if (!conditional)
                _defined[(half, index)] = _defined.GetValueOrDefault((half, index)) | LaneMask(mask);
            if (!_values.TryGetValue((half, index), out var previous)) return;
            if (conditional || !previous.Mask.All(mask.Contains))
                Materialize(half, index);
            else
                _values.Remove((half, index));
        }

        public void Define(bool half, int index, string mask, string name, string expression)
        {
            source.Body.AppendLine($"    {VectorType(half, mask.Length)} {name} = {expression};");
            _values[(half, index)] = (name, mask);
        }

        public string Register(bool half, int index, bool zeroInitialize = false) =>
            source.Local($"{(half ? "H" : "R")}{index}", half ? "half4" : "float4", zeroInitialize);

        public void MaterializeAll()
        {
            foreach (var key in _values.Keys.ToArray())
                Materialize(key.Half, key.Index);
        }

        private void Materialize(bool half, int index)
        {
            if (_values.Remove((half, index), out var value))
                source.Body.AppendLine($"    {Register(half, index)}{SwizzleSuffix(value.Mask)} = {value.Name};");
        }
    }

    private static string VectorType(bool half, int width) =>
        (half ? "half" : "float") + (width == 1 ? "" : width.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
