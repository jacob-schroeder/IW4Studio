using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IW4.Studio.MapEditor.Compilation.Collision;
using IW4.Studio.MapEditor.Compilation.Glass;
using IW4.Studio.MapEditor.Compilation.Lighting;
using IW4.Studio.MapEditor.Compilation.RenderWorld;
using IW4.Studio.MapEditor.Compilation.RenderWorld.Visibility;
using IW4.Studio.MapEditor.Editing.Objects;
using IW4.Studio.MapEditor.SourceDocuments;

namespace IW4.Studio.MapEditor.Compilation.TargetAcceptance;

/// <summary>
/// Adapts the bounded .iw4scene 1.0 source contract to the existing
/// mp_terminal minimal-target compiler. It is the only source-format-aware
/// component in the target pipeline; downstream builders continue to consume
/// immutable semantic compiler inputs.
/// </summary>
public static class MpTerminalMinimalTargetProbeSceneCompiler
{
    public static MpTerminalMinimalTargetProbeCompilation Compile(
        Iw4SceneDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        (
            AuthoredIndexedRenderMeshSource renderSource,
            IReadOnlyList<AuthoredConvexBrushCollisionSource>
                collisionSources,
            GfxWorldTargetMaterialDependencyEvidence material) =
                RequireSupportedSource(source);

        CollisionStructuralCandidate collision =
            CollisionStructuralCandidateBuilder.Compile(
                source.DocumentId,
                source.DocumentRevision,
                source.MapAssetName,
                collisionSources);
        RenderWorldStructuralCandidate render =
            RenderWorldStructuralCandidateBuilder.Compile(
                source.DocumentId,
                source.DocumentRevision,
                source.MapAssetName,
                [renderSource]);
        RenderWorldVisibilityCandidate visibility =
            RenderWorldVisibilityCandidateBuilder.Compile(
                collision,
                render);

        MapCompilerContentIdentityInput contentIdentityInput =
            new(
                source.MapAssetName,
                source.CompilerProfile,
                MapCompilerContractManifests
                    .MinimalMultiplayerTargetProbe
                    .Components,
                ComputeSemanticSourceDigest(
                    renderSource,
                    collisionSources),
                ComputeSettingsDigest(),
                material.DependencyDigest);
        MapPrimaryChecksumAssignment checksumAssignment =
            MapPrimaryChecksumPolicy.ComputeStudioCanonical(
                MapCompilerContentIdentityCalculator.Compute(
                    contentIdentityInput));
        MapSpatialTargetAcceptanceAssembly spatial =
            MapSpatialTargetAcceptanceAssembler.Assemble(
                visibility,
                checksumAssignment);
        GfxWorldNoBakeLightingCandidate lighting =
            GfxWorldNoBakeLightingAssembler.Compile(spatial);
        MinimalMultiplayerMapTargetProbeCandidate candidate =
            MinimalMultiplayerMapTargetProbeAssembler.Compile(
                lighting,
                contentIdentityInput);
        MinimalMultiplayerMapTargetMaterialResolution
            materialResolution =
                MinimalMultiplayerMapTargetMaterialResolver.Resolve(
                    candidate,
                    material);
        MinimalMultiplayerMapManagedRoundTripEvidence managedRoundTrip =
            MinimalMultiplayerMapManagedRoundTripVerifier.Verify(
                candidate);
        MinimalMultiplayerMapRuntimeSupportCompilation runtimeSupport =
            MinimalMultiplayerMapRuntimeSupportCompiler.Compile(
                source.MapAssetName,
                source.TargetZoneName,
                source.StartupProfile,
                checksumAssignment.Checksum);

        return new MpTerminalMinimalTargetProbeCompilation(
            contentIdentityInput,
            checksumAssignment,
            candidate,
            materialResolution,
            managedRoundTrip,
            runtimeSupport);
    }

    private static (
        AuthoredIndexedRenderMeshSource Render,
        IReadOnlyList<AuthoredConvexBrushCollisionSource> Collisions,
        GfxWorldTargetMaterialDependencyEvidence Material)
        RequireSupportedSource(Iw4SceneDocument source)
    {
        if (source.FormatVersion != Iw4SceneFormat.CurrentVersion ||
            !string.Equals(
                source.MapAssetName,
                MpTerminalMinimalTargetProbeFactory.MapAssetName,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.TargetZoneName,
                MpTerminalMinimalTargetProbeFactory.TargetZoneName,
                StringComparison.Ordinal) ||
            source.CompilerProfile !=
                MapCompilerProfiles.MinimalMultiplayerTargetProbe ||
            !string.Equals(
                source.EntityProfileIdentity,
                MinimalMultiplayerMapTargetProbeCandidate
                    .EntityProfileIdentity,
                StringComparison.Ordinal) ||
            source.StartupProfile !=
                MinimalMultiplayerMapTargetStartupProfile
                    .OfflineSplitScreenFreeForAll ||
            source.RenderMeshes.Count != 1 ||
            source.CollisionSources.Count == 0 ||
            source.RenderMeshes[0].Ownership is not
                StandaloneWorldRenderMeshOwnership ||
            source.CollisionSources.Any(value =>
                value is not AuthoredConvexBrushCollisionSource ||
                value.Ownership is not
                    StandaloneWorldCollisionSourceOwnership))
        {
            throw new NotSupportedException(
                "The bounded mp_terminal scene compiler requires .iw4scene " +
                "1.0, the minimal target profile, one standalone render " +
                "mesh, one or more standalone convex collision brushes, " +
                "the fixed " +
                "worldspawn/deathmatch/intermission profile, and offline " +
                "split-screen Free-for-All startup.");
        }

        AuthoredIndexedRenderMeshSource render = source.RenderMeshes[0];
        AuthoredConvexBrushCollisionSource[] collisions =
            source.CollisionSources
                .Cast<AuthoredConvexBrushCollisionSource>()
                .ToArray();
        GfxWorldTargetMaterialDependencyEvidence material =
            GfxWorldTargetMaterialDependencyCatalog
                .CommonMpChemLightGlow;
        if (!string.Equals(
                render.SymbolicMaterialName,
                material.AssetKey.LogicalName,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "The bounded mp_terminal scene compiler supports only the " +
                "target-observed common_mp material dependency.");
        }

        return (render, collisions, material);
    }

    private static MapCompilerSha256Digest ComputeSemanticSourceDigest(
        AuthoredIndexedRenderMeshSource render,
        IReadOnlyList<AuthoredConvexBrushCollisionSource> collisions)
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(
            hash,
            "domain",
            "iw4-studio.mp-terminal-minimal-target-probe-source/v1");
        Append(
            hash,
            "map",
            MpTerminalMinimalTargetProbeFactory.MapAssetName);
        Append(hash, "render-object", render.ObjectId.Value);
        Append(hash, "render-ownership", (int)render.Ownership.Kind);
        Append(hash, "render-material", render.SymbolicMaterialName);
        Append(hash, "render-winding", (int)render.TriangleWinding);
        Append(hash, "render-vertex-count", render.Vertices.Count);
        foreach (AuthoredRenderVertex vertex in render.Vertices)
        {
            Append(hash, "render-position-x", vertex.Position.X);
            Append(hash, "render-position-y", vertex.Position.Y);
            Append(hash, "render-position-z", vertex.Position.Z);
            Append(hash, "render-color-r", vertex.Color.Red);
            Append(hash, "render-color-g", vertex.Color.Green);
            Append(hash, "render-color-b", vertex.Color.Blue);
            Append(hash, "render-color-a", vertex.Color.Alpha);
            Append(
                hash,
                "render-texture-u",
                vertex.TextureCoordinates.U);
            Append(
                hash,
                "render-texture-v",
                vertex.TextureCoordinates.V);
            Append(
                hash,
                "render-lightmap-u",
                vertex.LightmapCoordinates.U);
            Append(
                hash,
                "render-lightmap-v",
                vertex.LightmapCoordinates.V);
            Append(hash, "render-normal-x", vertex.Normal.X);
            Append(hash, "render-normal-y", vertex.Normal.Y);
            Append(hash, "render-normal-z", vertex.Normal.Z);
            Append(hash, "render-tangent-x", vertex.Tangent.X);
            Append(hash, "render-tangent-y", vertex.Tangent.Y);
            Append(hash, "render-tangent-z", vertex.Tangent.Z);
        }
        Append(hash, "render-triangle-count", render.Triangles.Count);
        foreach (AuthoredIndexedRenderTriangle triangle in render.Triangles)
        {
            Append(hash, "render-index-0", triangle.Index0);
            Append(hash, "render-index-1", triangle.Index1);
            Append(hash, "render-index-2", triangle.Index2);
        }

        foreach (AuthoredConvexBrushCollisionSource collision in collisions)
        {
            Append(hash, "collision-object", collision.ObjectId.Value);
            Append(
                hash,
                "collision-ownership",
                (int)collision.Ownership.Category);
            Append(hash, "collision-contents", collision.Contents);
            Append(hash, "collision-face-count", collision.Faces.Count);
            foreach (AuthoredConvexBrushFace face in collision.Faces)
            {
                Append(hash, "collision-plane-x", face.Plane.Normal.X);
                Append(hash, "collision-plane-y", face.Plane.Normal.Y);
                Append(hash, "collision-plane-z", face.Plane.Normal.Z);
                Append(
                    hash,
                    "collision-plane-distance",
                    face.Plane.Distance);
                Append(
                    hash,
                    "collision-material",
                    face.Material.ExactName);
                Append(
                    hash,
                    "collision-surface-flags",
                    face.Material.SurfaceFlags);
                Append(
                    hash,
                    "collision-material-contents",
                    face.Material.Contents);
                Append(
                    hash,
                    "collision-winding-count",
                    face.Winding.Count);
                foreach (MapVector3 vertex in face.Winding)
                {
                    Append(hash, "collision-winding-x", vertex.X);
                    Append(hash, "collision-winding-y", vertex.Y);
                    Append(hash, "collision-winding-z", vertex.Z);
                }
            }
        }
        Append(
            hash,
            "entity-profile",
            MinimalMultiplayerMapTargetProbeCandidate
                .EntityProfileIdentity);

        return Digest(hash);
    }

    private static MapCompilerSha256Digest ComputeSettingsDigest()
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(
            hash,
            "domain",
            "iw4-studio.mp-terminal-minimal-target-probe-settings/v1");
        Append(
            hash,
            "render-compiler",
            RenderWorldStructuralProfile.CompilerIdentity);
        Append(
            hash,
            "visibility-compiler",
            RenderWorldVisibilityProfile.CompilerIdentity);
        Append(
            hash,
            "collision-spatial-policy",
            CollisionConservativeWorldSpatialCompiler.PolicyIdentity);
        Append(
            hash,
            "spatial-assembly",
            MapSpatialTargetAcceptanceAssembly
                .AssemblyProfileIdentity);
        Append(
            hash,
            "lighting-compiler",
            GfxWorldNoBakeLightingProfile.CompilerIdentity);
        Append(
            hash,
            "primary-light-ordinal-plan",
            PrimaryLightOrdinalPlan.CompilerIdentity);
        Append(
            hash,
            "glass-identity-allocator",
            GlassPieceIdentityAllocator.CompilerIdentity);
        Append(
            hash,
            "empty-glass-domain-compiler",
            EmptyGlassDomainCompiler.CompilerIdentity);
        Append(
            hash,
            "map-graph-compiler",
            MinimalMultiplayerMapTargetProbeCandidate.CompilerIdentity);
        Append(
            hash,
            "material-resolver",
            MinimalMultiplayerMapTargetMaterialResolver.CompilerIdentity);
        Append(
            hash,
            "runtime-support-compiler",
            MinimalMultiplayerMapRuntimeSupportCompiler
                .CompilerIdentity);

        // Compatibility salts from the original target fixture remain in
        // profile version 1 so existing candidate bytes and checksum identity
        // do not change when source persistence is introduced.
        Append(
            hash,
            "floor-half-extent",
            MpTerminalMinimalTargetProbeFactory.FloorHalfExtent);
        Append(
            hash,
            "collision-slab-half-depth",
            MpTerminalMinimalTargetProbeFactory
                .CollisionSlabHalfDepth);

        return Digest(hash);
    }

    private static MapCompilerSha256Digest Digest(
        IncrementalHash hash) =>
        new(
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());

    private static void Append(
        IncrementalHash hash,
        string tag,
        string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes, bigEndian: true, out int written) ||
            written != bytes.Length)
        {
            throw new InvalidDataException(
                "A stable target-probe Guid could not be serialized.");
        }
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        AppendHeader(hash, tag, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void Append(
        IncrementalHash hash,
        string tag,
        float value) =>
        Append(
            hash,
            tag,
            unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void Append(
        IncrementalHash hash,
        string tag,
        byte value) =>
        Append(hash, tag, (uint)value);

    private static void AppendHeader(
        IncrementalHash hash,
        string tag,
        int payloadLength)
    {
        byte[] tagBytes = Encoding.UTF8.GetBytes(tag);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, tagBytes.Length);
        hash.AppendData(length);
        hash.AppendData(tagBytes);
        BinaryPrimitives.WriteInt32BigEndian(length, payloadLength);
        hash.AppendData(length);
    }
}
