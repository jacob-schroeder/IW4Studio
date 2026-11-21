using System.Numerics;
using IW4.Formats.XModel;
using IW4.Game.Assets.XModel;
using IW4.Game.Codecs.XModel;
using IW4.Game.Math;

namespace IW4.Studio.Documents;

public sealed partial class XModelDraft
{
    internal static XModelAsset ExpandBoundsForSurfs(XModelAsset source, XModelSurfsAsset editedSurfs,
        IReadOnlyList<int> changedSurfaceIndices)
    {
        var root = new SurfaceEnvelope(source.Bounds, source.Radius * source.Radius);
        var bones = new SurfaceEnvelope?[source.BoneInfo.Count];
        foreach (int index in changedSurfaceIndices)
        {
            XSurface surface = editedSurfs.Surfaces[index];
            if (!XModelSurfaceSkinningProjector.TryProject(surface, source.NumBones, out var weights, out var reason))
                throw new InvalidDataException($"Cannot update bounds for {source.Name}, surface {index + 1}. {reason}");
            for (int i = 0; i < surface.VertCount; i++)
            {
                if (!XSurfaceVertexCodec.TryReadPosition(surface.Verts0, i, out var position))
                    throw new InvalidDataException($"Surface {index + 1} contains an invalid position.");
                root.Include(position);
                foreach (var influence in weights[i])
                {
                    if (influence.Weight <= 0) continue;
                    int bone = influence.BoneIndex;
                    if ((uint)bone >= bones.Length || (uint)bone >= source.BaseMat.Count)
                        throw new InvalidDataException($"The model has no bind pose or bounds for bone {bone}.");
                    if (bones[bone] is null)
                    {
                        var bind = source.BaseMat[bone];
                        float length = new Quaternion(bind.Quat.X, bind.Quat.Y, bind.Quat.Z, bind.Quat.W).LengthSquared();
                        if (!float.IsFinite(length) || length <= 0)
                            throw new InvalidDataException($"The model has an invalid bind rotation for bone {bone}.");
                        var info = source.BoneInfo[bone];
                        bones[bone] = new SurfaceEnvelope(info.Bounds, info.RadiusSquared);
                    }
                    bones[bone]!.Include(XModelAssemblyCompiler.ToBoneLocal(position, source.BaseMat[bone]));
                }
            }
        }
        if (!root.Changed && !bones.Any(b => b?.Changed == true)) return source;
        var infos = source.BoneInfo.Select((info, index) => bones[index] is { Changed: true } expanded
            ? new XBoneInfo(expanded.Bounds(), expanded.RadiusSquared) : info).ToArray();
        return CopyRoot(source, boundsOverride: root.Bounds(),
            radiusOverride: root.RadiusSquared > source.Radius * source.Radius ? MathF.Sqrt(root.RadiusSquared) : source.Radius,
            boneInfoOverride: infos);
    }

    // Retain any padding and coverage already needed by the model's other surfaces and LODs.
    private sealed class SurfaceEnvelope
    {
        private readonly Bounds _original;
        private Vector3 _min, _max;
        private bool _boxChanged;
        public bool Changed { get; private set; }
        public float RadiusSquared { get; private set; }

        public SurfaceEnvelope(Bounds bounds, float radiusSquared)
        {
            _original = bounds;
            var center = new Vector3(bounds.MidPoint.X, bounds.MidPoint.Y, bounds.MidPoint.Z);
            var half = new Vector3(bounds.HalfSize.X, bounds.HalfSize.Y, bounds.HalfSize.Z);
            _min = center - half;
            _max = center + half;
            RadiusSquared = radiusSquared;
        }

        public void Include(Vector3 p)
        {
            float distance = p.LengthSquared();
            if (!float.IsFinite(distance))
                throw new InvalidDataException("The edited surface would give the model non-finite bounds.");
            var min = Vector3.Min(_min, p);
            var max = Vector3.Max(_max, p);
            _boxChanged |= min != _min || max != _max;
            Changed |= _boxChanged || distance > RadiusSquared;
            _min = min;
            _max = max;
            RadiusSquared = Math.Max(RadiusSquared, distance);
        }

        public Bounds Bounds()
        {
            if (!_boxChanged) return _original;
            Vector3 center = _min * .5f + _max * .5f;
            Vector3 half = Vector3.Max(center - _min, _max - center);
            return new Bounds
            {
                MidPoint = new Vec3 { X = center.X, Y = center.Y, Z = center.Z },
                HalfSize = new Vec3 { X = half.X, Y = half.Y, Z = half.Z }
            };
        }
    }
}
