using System.Globalization;
using System.Numerics;
using System.Text;
using MapConverter.Game.IW3.PC.DynamicEntities;

namespace MapConverter.Game.IW3.PC.Conversion;

internal sealed record Iw3WorldFxCompilation(
    IReadOnlyDictionary<string, string> RawFilePaths,
    IReadOnlyList<string> EffectNames,
    IReadOnlyList<string> SoundNames,
    IReadOnlyList<string> ModelNames);

internal static class Iw3WorldFxCompiler
{
    private const string PaperEffect = "props/copypaper_box_exp";
    private const string ElectricalEffect = "props/electricbox_small";
    private const string ElectricalSound = "exp_fusebox_sparks";

    internal static Iw3WorldFxCompilation Compile(
        string assetDirectory, Iw3DynamicEntitySourceData dynamicEntities, string mapName, string outputDirectory)
    {
        Iw3DynamicEntitySource[] paperBoxes = dynamicEntities.Definitions.SelectMany(list => list)
            .Where(item => item.DestroyFxName == "destructibles/fx_dest_paper_pile").ToArray();
        if (paperBoxes.Any(item => item.Type != 1 || item.Health <= 0 || item.XModelName is null ||
            item.BrushModel != 0 || item.PhysicsBrushModel != 0 || item.DestroyPiecesName is not null))
            throw new InvalidDataException("The source paper FX requires a damageable clutter model without brush or replacement-piece dependencies.");

        var rawFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var effects = new List<string>();
        var sounds = new List<string>();
        string[] models = paperBoxes.Select(item => item.XModelName ??
                throw new InvalidDataException("A paper FX source has no model."))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var script = new StringBuilder("main()\n{\n");
        if (paperBoxes.Length != 0)
        {
            effects.Add(PaperEffect);
            script.AppendLine($"\tpaperfx = loadfx({Quote(PaperEffect)});");
            foreach (string model in models)
                script.AppendLine($"\tprecachemodel({Quote(model)});");
            foreach (Iw3DynamicEntitySource box in paperBoxes)
            {
                var sourceRotation = new Quaternion(box.Quat[0], box.Quat[1], box.Quat[2], box.Quat[3]);
                if (!float.IsFinite(sourceRotation.LengthSquared()) || sourceRotation.LengthSquared() < 0.000001f)
                    throw new InvalidDataException("A paper prop has an invalid source rotation.");
                Quaternion rotation = Quaternion.Normalize(sourceRotation);
                // IW angles are pitch/yaw/roll, with forward Z = -sin(pitch).
                Vector3 forward = Vector3.Transform(Vector3.UnitX, rotation);
                Vector3 left = Vector3.Transform(Vector3.UnitY, rotation);
                Vector3 up = Vector3.Transform(Vector3.UnitZ, rotation);
                float horizontal = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);
                if (horizontal < 0.0001f)
                    throw new InvalidDataException("A vertical paper prop cannot be represented by this script placement.");
                float degrees = 180f / MathF.PI;
                float[] angles = [-MathF.Atan2(forward.Z, horizontal) * degrees,
                    MathF.Atan2(forward.Y, forward.X) * degrees,
                    MathF.Atan2(left.Z, up.Z) * degrees];
                script.AppendLine($"\tbox = spawn(\"script_model\", {Vector(box.Origin)});");
                script.AppendLine($"\tbox setmodel({Quote(box.XModelName ?? throw new InvalidDataException("Missing paper model."))});");
                script.AppendLine($"\tbox.angles = {Vector(angles)};");
                script.AppendLine("\tbox setcandamage(true);");
                script.AppendLine($"\tbox thread paper_damage(paperfx, {box.Health.ToString(CultureInfo.InvariantCulture)});");
            }
        }

        string electricalPath = Path.Combine(assetDirectory, Iw3PcMapConverter.ElectricBoxRawFileName);
        if (File.Exists(electricalPath))
        {
            string source = File.ReadAllText(electricalPath);
            source = ReplaceRequired(source,
                $"loadfx (\"{Iw3PcMapConverter.ElectricBoxFxName}\")", $"loadfx ({Quote(ElectricalEffect)})");
            source = ReplaceRequired(source, "playsound(\"explo_2\")", $"playsound({Quote(ElectricalSound)})");
            source = ReplaceRequired(source, "window = getent(self.target,\"targetname\");",
                "window = getent(self.target,\"targetname\");\n\tif (!isdefined(window))\n\t\treturn;");
            source = ReplaceRequired(source, "self waittill (\"damage\",amount);",
                "self waittill (\"damage\",amount);\n\t\tif (!isdefined(amount) || amount <= 0)\n\t\t\tcontinue;");
            string outputPath = Path.Combine(outputDirectory, "_electricbox.gsc");
            File.WriteAllText(outputPath, source);
            rawFiles.Add(Iw3PcMapConverter.ElectricBoxRawFileName, outputPath);
            effects.Add(ElectricalEffect);
            sounds.Add(ElectricalSound);
            script.AppendLine("\tmaps\\mp\\_electricbox::main();");
        }
        script.AppendLine("}");
        if (paperBoxes.Length != 0)
        {
            // Restore the source damage event without enabling all client physics.
            // The script proxy remains stationary and uses source placement/health.
            script.AppendLine("""

                paper_damage(effect, health)
                {
                    level endon("game_ended");
                    while (health > 0)
                    {
                        self waittill("damage", damage);
                        if (!isdefined(damage) || damage <= 0)
                            continue;
                        health -= damage;
                    }
                    playfx(effect, self.origin, anglestoforward(self.angles), anglestoup(self.angles));
                    self delete();
                }
                """);
        }
        if (effects.Count != 0)
        {
            string outputPath = Path.Combine(outputDirectory, mapName + "_fx.gsc");
            File.WriteAllText(outputPath, script.ToString());
            rawFiles.Add($"maps/mp/{mapName}_fx.gsc", outputPath);
        }
        return new Iw3WorldFxCompilation(rawFiles, effects, sounds, models);
    }

    private static string ReplaceRequired(string source, string before, string after)
    {
        int index = source.IndexOf(before, StringComparison.Ordinal);
        if (index < 0 || source.LastIndexOf(before, StringComparison.Ordinal) != index)
            throw new InvalidDataException("The source electrical FX script has an unsupported initialization or damage handler.");
        return source.Replace(before, after, StringComparison.Ordinal);
    }

    private static string Quote(string value)
    {
        if (value.Length == 0 || value.Any(char.IsControl))
            throw new InvalidDataException("A damage-FX asset has an invalid script name.");
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Vector(IReadOnlyList<float> values) => "(" +
        string.Join(", ", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + ")";
}
