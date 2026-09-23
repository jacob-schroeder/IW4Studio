using System.Numerics;
using IW4.Render.EditorPreview;
using IW4.Render.OpenGl.Presentation;
using Silk.NET.OpenGL;

namespace IW4.Render.OpenGl;

public sealed unsafe partial class SilkOpenGlMapRenderer
{
    private const int SelectionOutlineVertexFloatCount = 6;
    private GlMesh _editorSelectionOutlineMesh;
    private MapRenderEditorSelectionOutline? _editorSelectionOutline;
    private MapRenderEditorSelectionOutline? _uploadedEditorSelectionOutline;

    /// <summary>
    /// Replaces the renderer-local editor selection visual. The projection is
    /// drawn into the scene target and depth-tested with the map, but is never
    /// incorporated into semantic scene state or persisted assets.
    /// </summary>
    public void SetEditorSelectionOutline(
        MapRenderEditorSelectionOutline? outline)
    {
        ThrowIfUnavailable();
        if (!_loaded)
        {
            throw new InvalidOperationException(
                "A map scene must be loaded before setting an editor selection outline.");
        }
        if (outline is { IsValid: false })
        {
            throw new ArgumentException(
                "The editor selection outline must be a valid render-space projection.",
                nameof(outline));
        }

        _editorSelectionOutline = outline;
    }

    private GlMesh CreateEditorSelectionOutlineMesh()
    {
        float[] vertices = new float[
            MapRenderEditorSelectionOutlineGeometry.CornerCount *
            SelectionOutlineVertexFloatCount];
        uint[] indices =
            MapRenderEditorSelectionOutlineGeometry.LineIndices.ToArray();
        return CreateMesh(
            vertices,
            indices,
            BufferUsageARB.DynamicDraw);
    }

    private void DrawEditorSelectionOutline(
        EditorPresentationFrame? frame,
        Matrix4x4 viewProjection)
    {
        if (_editorSelectionOutline is not { } outline ||
            _editorSelectionOutlineMesh.IndexCount == 0)
        {
            return;
        }

        UploadEditorSelectionOutline(outline);
        if (frame is { } presentationFrame)
        {
            _state.BindFramebuffer(
                FramebufferTarget.Framebuffer,
                presentationFrame.SceneTarget.CombinedFramebufferHandle);
            _gl.DrawBuffer(DrawBufferMode.ColorAttachment0);
            _state.Viewport(
                presentationFrame.SceneTarget.ViewportX,
                presentationFrame.SceneTarget.ViewportY,
                presentationFrame.SceneTarget.ViewportWidth,
                presentationFrame.SceneTarget.ViewportHeight);
            var antialiasing =
                presentationFrame.SceneTarget.Antialiasing;
            _state.SetEnabled(
                EnableCap.Multisample,
                antialiasing.MultisampleEnabled);
            _state.SetEnabled(EnableCap.SampleMask, true);
            _state.SampleMask(
                antialiasing.HostSampleMaskWordIndex,
                antialiasing.HostSampleMaskWord);
            _state.SetEnabled(
                EnableCap.SampleAlphaToCoverage,
                antialiasing.AlphaToCoverageEnabled);
            _state.SetEnabled(
                EnableCap.SampleAlphaToOne,
                antialiasing.AlphaToOneEnabled);
        }
        else
        {
            _state.BindFramebuffer(
                FramebufferTarget.Framebuffer,
                _hostFramebuffer);
            _gl.DrawBuffer(
                _hostFramebuffer == 0
                    ? DrawBufferMode.Back
                    : DrawBufferMode.ColorAttachment0);
            _state.Viewport(0, 0, _hostWidth, _hostHeight);
        }

        // Own the complete fixed state needed by this editor-only line pass.
        // Depth writes stay disabled so the visual cannot affect subsequent
        // scene or presentation work.
        _state.SetEnabled(EnableCap.FramebufferSrgb, false);
        _state.SetEnabled(EnableCap.ScissorTest, false);
        _state.SetEnabled(EnableCap.DepthTest, true);
        _state.DepthFunc(DepthFunction.Lequal);
        _state.DepthMask(false);
        _state.SetEnabled(EnableCap.StencilTest, false);
        _state.SetEnabled(EnableCap.Blend, false);
        _state.SetEnabled(EnableCap.CullFace, false);
        _state.SetEnabled(EnableCap.PolygonOffsetFill, false);
        _state.SetEnabled(EnableCap.PolygonOffsetLine, false);
        _state.SetEnabled(EnableCap.PolygonOffsetPoint, false);
        _state.PolygonMode(PolygonMode.Fill);
        _state.ColorMask(true, true, true, true);
        _state.LineWidth(_wireframeEffectiveLineWidth);
        _state.UseProgram(_solidProgram);
        _state.UniformMatrix4(
            _solidViewProjectionLocation,
            viewProjection);
        _state.Uniform1(_solidUseInstancingLocation, 0);
        Draw(_editorSelectionOutlineMesh, PrimitiveType.Lines);

        _state.DepthMask(true);
        _state.LineWidth(1f);
    }

    private void UploadEditorSelectionOutline(
        MapRenderEditorSelectionOutline outline)
    {
        if (_uploadedEditorSelectionOutline == outline)
            return;

        Span<Vector3> corners =
            stackalloc Vector3[
                MapRenderEditorSelectionOutlineGeometry.CornerCount];
        MapRenderEditorSelectionOutlineGeometry.WriteCorners(
            outline,
            corners);
        Span<float> vertices =
            stackalloc float[
                MapRenderEditorSelectionOutlineGeometry.CornerCount *
                SelectionOutlineVertexFloatCount];
        for (int index = 0; index < corners.Length; index++)
        {
            int offset = index * SelectionOutlineVertexFloatCount;
            Vector3 corner = corners[index];
            vertices[offset] = corner.X;
            vertices[offset + 1] = corner.Y;
            vertices[offset + 2] = corner.Z;
            vertices[offset + 3] = outline.Color.X;
            vertices[offset + 4] = outline.Color.Y;
            vertices[offset + 5] = outline.Color.Z;
        }

        _state.BindArrayBuffer(
            _editorSelectionOutlineMesh.VertexBuffer);
        fixed (float* vertexPointer = vertices)
        {
            _gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                checked((nuint)(vertices.Length * sizeof(float))),
                vertexPointer);
        }
        _uploadedEditorSelectionOutline = outline;
    }

    private void ResetEditorSelectionOutline()
    {
        _editorSelectionOutline = null;
        _uploadedEditorSelectionOutline = null;
    }
}
