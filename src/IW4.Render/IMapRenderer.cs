using System.Numerics;
using IW4.Assets.Assets.ColMap;
using IW4.Assets.Assets.GfxMap;
using IW4.FastFiles.Zone;
using IW4.Runtime.Database;

namespace IW4.Render;

public interface IMapRenderer : IDisposable
{
    void Load(MapRenderScene scene);
    void Resize(int width, int height);
    void Resize(MapRenderSurfaceExtents extents)
    {
        if (!extents.IsValid)
            throw new ArgumentOutOfRangeException(nameof(extents));
        if (extents.RequiresHostScale)
        {
            throw new NotSupportedException(
                "This renderer does not implement split scene/host extents.");
        }

        Resize(extents.SceneTarget.Width, extents.SceneTarget.Height);
    }
    void Render(RenderCamera camera);
}
