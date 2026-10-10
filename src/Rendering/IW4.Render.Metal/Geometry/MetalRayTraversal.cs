using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using IW4.Render.Geometry;
using IW4.Render.Metal.Native;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace IW4.Render.Metal;

// The acceleration structure intersects indexed world and static-model triangles.
// Material sampling remains with the caller.
[SupportedOSPlatform("macos")]
public sealed unsafe class MetalRayTraversal : GpuRayTraversal
{
    private const int MaximumSceneBytes = 128 * 1024 * 1024;
    public const int SkySampleCapacity = 8192;
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Primitive(int surface, int corner)
    {
        public readonly int Surface = surface;
        public readonly int Corner = corner;
    }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public readonly struct SkyMaterial(int imageOffset, int width, int height, int faces,
        uint sampler, uint alphaTest, uint flags, uint shadowAlphaTest)
    {
        [FieldOffset(0)] public readonly int ImageOffset = imageOffset;
        [FieldOffset(4)] public readonly int Width = width;
        [FieldOffset(8)] public readonly int Height = height;
        [FieldOffset(12)] public readonly int Faces = faces;
        [FieldOffset(16)] public readonly uint Sampler = sampler;
        [FieldOffset(20)] public readonly uint AlphaTest = alphaTest;
        [FieldOffset(24)] public readonly uint Flags = flags;
        [FieldOffset(28)] public readonly uint ShadowAlphaTest = shadowAlphaTest;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public readonly struct SkyVertex(Vector2 texture, float alpha)
    {
        [FieldOffset(0)] public readonly Vector2 Texture = texture;
        [FieldOffset(8)] public readonly float Alpha = alpha;
    }
    private const string ShaderSource = """
        #include <metal_stdlib>
        #include <metal_raytracing>
        using namespace metal;
        using namespace raytracing;

        struct RayData {
            packed_float3 origin;
            float padding0;
            packed_float3 direction;
            float padding1;
        };
        struct SurfaceData {
            packed_float3 normal;
            int vertexOffset;
            int vertexCount;
            uint flags;
        };
        struct PrimitiveData { int surface; int corner; };
        struct HitData {
            int surface;
            float distance;
            int corner;
            packed_float2 coordinates;
        };
        struct SkyMaterialData {
            int imageOffset;
            int width;
            int height;
            int faces;
            uint sampler;
            uint alphaTest;
            uint flags;
            uint shadowAlphaTest;
        };
        struct SkyVertexData { packed_float2 texture; float alpha; float padding; };

        int textureIndex(int coordinate, int length, bool clampEdge) {
            return clampEdge ? clamp(coordinate, 0, length - 1) :
                ((coordinate % length) + length) % length;
        }
        float4 readTexel(device const uchar* images, SkyMaterialData material,
                         int face, int x, int y, bool clampU, bool clampV) {
            x = textureIndex(x, material.width, clampU);
            y = textureIndex(y, material.height, clampV);
            int offset = material.imageOffset +
                ((face * material.height + y) * material.width + x) * 4;
            return float4(images[offset], images[offset + 1], images[offset + 2], images[offset + 3]) / 255.0f;
        }
        float4 sampleImage(device const uchar* images, SkyMaterialData material,
                           int face, float2 uv, bool sky) {
            bool clampU = sky || (material.sampler & 32u) != 0u;
            bool clampV = sky || (material.sampler & 64u) != 0u;
            uv.x = clampU ? clamp(uv.x, 0.0f, 1.0f) : uv.x - floor(uv.x);
            uv.y = clampV ? clamp(uv.y, 0.0f, 1.0f) : uv.y - floor(uv.y);
            float x = uv.x * float(material.width) - 0.5f;
            float y = uv.y * float(material.height) - 0.5f;
            if (!sky && (material.sampler & 7u) == 1u)
                return readTexel(images, material, face, int(floor(x + 0.5f)),
                    int(floor(y + 0.5f)), clampU, clampV);
            int x0 = int(floor(x)), y0 = int(floor(y));
            float4 a = mix(readTexel(images, material, face, x0, y0, clampU, clampV),
                readTexel(images, material, face, x0 + 1, y0, clampU, clampV), x - float(x0));
            float4 b = mix(readTexel(images, material, face, x0, y0 + 1, clampU, clampV),
                readTexel(images, material, face, x0 + 1, y0 + 1, clampU, clampV), x - float(x0));
            return mix(a, b, y - float(y0));
        }
        float3 sampleSky(device const uchar* images, SkyMaterialData material, float3 direction) {
            float3 a = abs(direction);
            int face;
            float s, t, major;
            if (a.x >= a.y && a.x >= a.z) {
                major = a.x;
                if (direction.x >= 0.0f) { face = 0; s = -direction.z; t = -direction.y; }
                else { face = 1; s = direction.z; t = -direction.y; }
            } else if (a.y >= a.z) {
                major = a.y;
                if (direction.y >= 0.0f) { face = 2; s = direction.x; t = direction.z; }
                else { face = 3; s = direction.x; t = -direction.z; }
            } else {
                major = a.z;
                if (direction.z >= 0.0f) { face = 4; s = direction.x; t = -direction.y; }
                else { face = 5; s = -direction.x; t = -direction.y; }
            }
            float4 texel = sampleImage(images, material, face,
                float2((s / major + 1.0f) * 0.5f, (t / major + 1.0f) * 0.5f), true);
            float3 color = texel.rgb * texel.a;
            return color * color;
        }

        kernel void bake_sky_irradiance(
            primitive_acceleration_structure triangles [[buffer(0)]],
            device const RayData* samples [[buffer(1)]],
            device packed_float3* output [[buffer(2)]],
            device const PrimitiveData* primitives [[buffer(3)]],
            constant uint& sampleCount [[buffer(4)]],
            device const SurfaceData* surfaces [[buffer(5)]],
            device const SkyMaterialData* materials [[buffer(6)]],
            device const SkyVertexData* attributes [[buffer(7)]],
            device const uchar* images [[buffer(8)]],
            device const packed_float3* directions [[buffer(9)]],
            constant float& ambientScale [[buffer(10)]],
            constant uint& worldSurfaceCount [[buffer(11)]],
            uint sampleIndex [[thread_position_in_grid]])
        {
            if (sampleIndex >= sampleCount) return;
            RayData receiver = samples[sampleIndex];
            float3 normal = float3(receiver.direction);
            float3 origin = float3(receiver.origin) + normal * 0.0625f;
            float3 sum = float3(0.0f);
            float totalWeight = 0.0f;
            for (uint index = 0u; index < 24u; ++index) {
                float3 direction = float3(directions[index]);
                float cosine = max(0.0f, dot(normal, direction));
                if (cosine <= 0.0f) continue;
                totalWeight += cosine;
                ray queryRay(origin, direction, 0.0f, 1.0e30f);
                intersection_query<triangle_data> query(queryRay, triangles);
                float nearest = 1.0e30f;
                float nearestBlend = 1.0e30f;
                int nearestSurface = -1;
                bool modelBlocked = false, hasBlend = false;
                while (query.next()) {
                    if (query.get_candidate_intersection_type() != intersection_type::triangle) continue;
                    float distance = query.get_candidate_triangle_distance();
                    if (!isfinite(distance) || distance < 0.0f) continue;
                    PrimitiveData primitive = primitives[query.get_candidate_primitive_id()];
                    SurfaceData surface = surfaces[primitive.surface];
                    if (uint(primitive.surface) >= worldSurfaceCount) {
                        if ((surface.flags & 4u) == 0u) continue;
                        float3 modelNormal = float3(surface.normal);
                        float facing = dot(modelNormal, -direction);
                        if (((surface.flags & 64u) != 0u && facing >= 0.0f) ||
                            ((surface.flags & 128u) != 0u && facing < 0.0f) ||
                            (distance <= 1.0e-4f && dot(modelNormal, direction) >= 0.0f)) continue;
                        if ((surface.flags & 8u) != 0u) {
                            SkyMaterialData modelMaterial = materials[primitive.surface];
                            float2 bary = query.get_candidate_triangle_barycentric_coord();
                            int offset = surface.vertexOffset;
                            SkyVertexData v0 = attributes[offset];
                            SkyVertexData v1 = attributes[offset + primitive.corner];
                            SkyVertexData v2 = attributes[offset + primitive.corner + 1];
                            float a = 1.0f - bary.x - bary.y;
                            float2 uv = float2(v0.texture) * a + float2(v1.texture) * bary.x +
                                float2(v2.texture) * bary.y;
                            float vertexAlpha = v0.alpha * a + v1.alpha * bary.x + v2.alpha * bary.y;
                            float alpha = clamp(sampleImage(images, modelMaterial, 0, uv, false).a *
                                vertexAlpha, 0.0f, 1.0f);
                            if ((modelMaterial.shadowAlphaTest == 1u && !(alpha > 0.0f)) ||
                                (modelMaterial.shadowAlphaTest == 2u && !(alpha < 128.0f / 255.0f)) ||
                                (modelMaterial.shadowAlphaTest == 3u && !(alpha >= 128.0f / 255.0f))) continue;
                        }
                        modelBlocked = true;
                        continue;
                    }
                    if (distance > nearest + 0.001f) continue;
                    if ((surface.flags & 2u) != 0u) continue;
                    float3 faceNormal = float3(surface.normal);
                    if ((surface.flags & 1u) == 0u) {
                        float facing = dot(faceNormal, direction);
                        if (((surface.flags & 16u) != 0u && facing >= 0.0f) ||
                            ((surface.flags & 32u) != 0u && facing < 0.0f)) continue;
                    }
                    if (distance <= 1.0e-4f && dot(faceNormal, direction) >= 0.0f) continue;
                    SkyMaterialData material = materials[primitive.surface];
                    if (material.alphaTest != 0u) {
                        float2 bary = query.get_candidate_triangle_barycentric_coord();
                        int offset = surface.vertexOffset;
                        SkyVertexData v0 = attributes[offset];
                        SkyVertexData v1 = attributes[offset + primitive.corner];
                        SkyVertexData v2 = attributes[offset + primitive.corner + 1];
                        float a = 1.0f - bary.x - bary.y;
                        float2 uv = float2(v0.texture) * a + float2(v1.texture) * bary.x +
                            float2(v2.texture) * bary.y;
                        float vertexAlpha = v0.alpha * a + v1.alpha * bary.x + v2.alpha * bary.y;
                        float alpha = clamp(sampleImage(images, material, 0, uv, false).a *
                            vertexAlpha, 0.0f, 1.0f);
                        if ((material.alphaTest == 1u && !(alpha > 0.0f)) ||
                            (material.alphaTest == 2u && !(alpha < 128.0f / 255.0f)) ||
                            (material.alphaTest == 3u && !(alpha >= 128.0f / 255.0f))) continue;
                    }
                    if ((material.flags & 1u) != 0u) {
                        nearestBlend = min(nearestBlend, distance);
                        hasBlend = true;
                        continue;
                    }
                    if (nearestSurface >= 0 && primitive.surface != nearestSurface &&
                        abs(distance - nearest) < 0.001f) {
                        output[sampleIndex] = packed_float3(as_type<float>(0x7fc00000u));
                        return;
                    }
                    if (distance < nearest || nearestSurface < 0 || primitive.surface < nearestSurface) {
                        nearest = distance;
                        nearestSurface = primitive.surface;
                    }
                }
                if (!modelBlocked && hasBlend && nearestBlend <= nearest + 0.001f) {
                    output[sampleIndex] = packed_float3(as_type<float>(0x7fc00000u));
                    return;
                }
                if (!modelBlocked && nearestSurface >= 0 &&
                    (surfaces[nearestSurface].flags & 1u) != 0u)
                    sum += sampleSky(images, materials[nearestSurface], direction) * cosine;
            }
            output[sampleIndex] = totalWeight > 0.0f ?
                packed_float3(sum * (ambientScale / totalWeight)) : packed_float3(0.0f);
        }

        kernel void collect_intersections(
            primitive_acceleration_structure triangles [[buffer(0)]],
            device const RayData* rays [[buffer(1)]],
            device HitData* results [[buffer(2)]],
            device const PrimitiveData* primitives [[buffer(3)]],
            constant uint& rayCount [[buffer(4)]],
            device const SurfaceData* surfaces [[buffer(5)]],
            constant uint& shadowMode [[buffer(6)]],
            device int* headers [[buffer(7)]],
            constant uint& worldSurfaceCount [[buffer(8)]],
            device const SkyMaterialData* materials [[buffer(9)]],
            device const SkyVertexData* attributes [[buffer(10)]],
            device const uchar* images [[buffer(11)]],
            constant uint& alphaReady [[buffer(12)]],
            uint rayIndex [[thread_position_in_grid]])
        {
            if (rayIndex >= rayCount) return;
            const uint row = rayIndex * 16u;
            const RayData input = rays[rayIndex];
            const float3 direction = float3(input.direction);
            ray queryRay(float3(input.origin), direction, 0.0f, 1.0e30f);
            intersection_query<triangle_data> query(queryRay, triangles);
            int corners[16];
            uint count = 0u;
            bool modelUncertain = false;
            while (query.next()) {
                if (query.get_candidate_intersection_type() != intersection_type::triangle)
                    continue;
                float distance = query.get_candidate_triangle_distance();
                if (!isfinite(distance) || distance < 0.0f) continue;
                PrimitiveData primitive = primitives[query.get_candidate_primitive_id()];
                bool model = uint(primitive.surface) >= worldSurfaceCount;
                if (model && shadowMode == 0u) continue;
                SurfaceData surface = surfaces[primitive.surface];
                if ((surface.flags & 2u) != 0u ||
                    (shadowMode != 0u && ((surface.flags & 1u) != 0u ||
                                          (surface.flags & 4u) == 0u))) continue;
                float3 normal = float3(surface.normal);
                if ((surface.flags & 1u) == 0u) {
                    float facing = dot(normal, shadowMode != 0u ? -direction : direction);
                    uint backFlag = shadowMode != 0u ? 64u : 16u;
                    uint frontFlag = shadowMode != 0u ? 128u : 32u;
                    if (((surface.flags & backFlag) != 0u && facing >= 0.0f) ||
                        ((surface.flags & frontFlag) != 0u && facing < 0.0f)) continue;
                }
                if (distance <= 1.0e-4f && dot(normal, direction) >= 0.0f) continue;
                if (shadowMode != 0u) {
                    if ((surface.flags & 8u) == 0u) {
                        headers[rayIndex] = -2;
                        return;
                    }
                    if (alphaReady != 0u) {
                        SkyMaterialData material = materials[primitive.surface];
                        float2 bary = query.get_candidate_triangle_barycentric_coord();
                        int offset = surface.vertexOffset;
                        SkyVertexData v0 = attributes[offset];
                        SkyVertexData v1 = attributes[offset + primitive.corner];
                        SkyVertexData v2 = attributes[offset + primitive.corner + 1];
                        float a = 1.0f - bary.x - bary.y;
                        float2 uv = float2(v0.texture) * a + float2(v1.texture) * bary.x +
                            float2(v2.texture) * bary.y;
                        float vertexAlpha = v0.alpha * a + v1.alpha * bary.x + v2.alpha * bary.y;
                        float alpha = clamp(sampleImage(images, material, 0, uv, false).a *
                            vertexAlpha, 0.0f, 1.0f);
                        if ((material.shadowAlphaTest == 1u && alpha > 0.0f) ||
                            (material.shadowAlphaTest == 2u && alpha < 128.0f / 255.0f) ||
                            (material.shadowAlphaTest == 3u && alpha >= 128.0f / 255.0f)) {
                            headers[rayIndex] = -2;
                            return;
                        }
                        continue;
                    }
                }
                if (model) {
                    modelUncertain = true;
                    continue;
                }
                uint existing = 0u;
                while (existing < count &&
                       results[row + existing].surface != primitive.surface) ++existing;
                if (existing < count) {
                    if (primitive.corner < corners[existing]) {
                        corners[existing] = primitive.corner;
                        results[row + existing].distance = distance;
                        results[row + existing].corner = primitive.corner;
                        results[row + existing].coordinates =
                            query.get_candidate_triangle_barycentric_coord();
                    }
                    continue;
                }
                if (count == 16u) {
                    headers[rayIndex] = -1;
                    return;
                }
                corners[count] = primitive.corner;
                results[row + count] = { primitive.surface, distance,
                    primitive.corner, query.get_candidate_triangle_barycentric_coord() };
                ++count;
                // Uncommitted nonopaque triangles keep every hit available.
            }
            headers[rayIndex] = modelUncertain ? -4 :
                shadowMode != 0u && count == 0u ? -3 : int(count);
        }
        """;

    private MTLDevice _device;
    private MTLCommandQueue _queue;
    private MTLComputePipelineState _pipeline;
    private MTLComputePipelineState _skyPipeline;
    private MTLAccelerationStructure _acceleration;
    private MTLBuffer _primitives;
    private MTLBuffer _surfaces;
    private MTLBuffer _vertices;
    private MTLBuffer _skyMaterials;
    private MTLBuffer _skyAttributes;
    private MTLBuffer _skyImages;
    private MTLBuffer _skyDirections;
    private MTLBuffer _skySamples;
    private MTLBuffer _skyOutput;
    private readonly MTLBuffer[] _rays = new MTLBuffer[2];
    private readonly MTLBuffer[] _headers = new MTLBuffer[2];
    private readonly MTLBuffer[] _results = new MTLBuffer[2];
    private readonly MTLCommandBuffer[] _pending = new MTLCommandBuffer[2];
    private readonly int[] _pendingRayCounts = new int[2];
    private int _submittedBatches;
    private int _readBatches;
    private bool _unavailable;
    private bool _skyReady;
    private bool _disposed;
    private int _worldSurfaceCount;
    private int _surfaceCount;

    private MetalRayTraversal() { }

    public override string Name => _skyReady
        ? "Metal GPU intersections, alpha shadows, and sky irradiance"
        : "Metal GPU ray intersections";
    public override int MaximumInFlightBatches => 2;

    public static MetalRayTraversal? TryCreate(
        ReadOnlySpan<Surface> surfaces,
        ReadOnlySpan<Vector3> vertices,
        int worldSurfaceCount,
        CancellationToken cancellationToken,
        out string? failureReason)
    {
        failureReason = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (worldSurfaceCount < 0 || worldSurfaceCount > surfaces.Length)
            throw new ArgumentOutOfRangeException(nameof(worldSurfaceCount));
        if (!OperatingSystem.IsMacOS() || surfaces.IsEmpty || vertices.IsEmpty ||
            (long)surfaces.Length * sizeof(Surface) > MaximumSceneBytes ||
            (long)vertices.Length * sizeof(Vector3) > MaximumSceneBytes)
            return null;

        long triangleCount = 0;
        for (int index = 0; index < surfaces.Length; index++)
        {
            ref readonly Surface surface = ref surfaces[index];
            if (surface.VertexOffset < 0 || surface.VertexCount < 3 ||
                (long)surface.VertexOffset + surface.VertexCount > vertices.Length)
                throw new ArgumentException("A ray surface has an invalid vertex range.", nameof(surfaces));
            if (!SupportedCoordinate(surface.Normal)) return null;
            triangleCount += surface.VertexCount - 2;
        }
        if (triangleCount * 3 * sizeof(uint) > MaximumSceneBytes ||
            triangleCount * sizeof(Primitive) > MaximumSceneBytes)
            return null;
        foreach (ref readonly Vector3 vertex in vertices)
            if (!SupportedCoordinate(vertex)) return null;

        var traversal = new MetalRayTraversal();
        traversal._worldSurfaceCount = worldSurfaceCount;
        traversal._surfaceCount = surfaces.Length;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            if (!traversal.Initialize(surfaces, vertices, (int)triangleCount, cancellationToken))
            {
                failureReason = traversal.FailureReason;
                traversal.Dispose();
                return null;
            }
            return traversal;
        }
        catch (Exception exception) when (exception is DllNotFoundException or
            EntryPointNotFoundException or BadImageFormatException or PlatformNotSupportedException)
        {
            traversal.Dispose();
            return null;
        }
        catch (InvalidOperationException exception)
        {
            failureReason = exception.Message;
            traversal.Dispose();
            return null;
        }
        catch
        {
            traversal.Dispose();
            throw;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private bool Initialize(
        ReadOnlySpan<Surface> surfaces,
        ReadOnlySpan<Vector3> vertices,
        int triangleCount,
        CancellationToken cancellationToken)
    {
        MetalNativeFrameworks.EnsureLoaded();
        _device = MTLDevice.CreateSystemDefaultDevice();
        if (_device.NativePtr == 0) return false;
        if (!_device.SupportsRaytracing || !_device.SupportsFamily(MTLGPUFamily.Apple9))
            return false;
        cancellationToken.ThrowIfCancellationRequested();

        _queue = _device.NewCommandQueue();
        if (_queue.NativePtr == 0)
        {
            FailureReason = "Metal ray command queue creation failed.";
            return false;
        }
        using var options = new MTLCompileOptions { FastMathEnabled = false };
        NSError shaderError = default;
        MTLLibrary library = _device.NewLibrary(ShaderSource, options, ref shaderError);
        if (library.NativePtr == 0 || shaderError.NativePtr != 0)
        {
            if (library.NativePtr != 0) library.Dispose();
            throw new InvalidOperationException($"Metal ray shader compilation failed: {Describe(shaderError)}");
        }
        try
        {
            MTLFunction function = library.NewFunction("collect_intersections");
            if (function.NativePtr == 0)
                throw new InvalidOperationException("Metal ray shader entry point is missing.");
            try
            {
                NSError pipelineError = default;
                _pipeline = _device.NewComputePipelineState(function, ref pipelineError);
                if (_pipeline.NativePtr == 0 || pipelineError.NativePtr != 0)
                {
                    if (_pipeline.NativePtr != 0)
                    {
                        _pipeline.Dispose();
                        _pipeline = default;
                    }
                    throw new InvalidOperationException($"Metal ray pipeline creation failed: {Describe(pipelineError)}");
                }
            }
            finally { function.Dispose(); }
            MTLFunction skyFunction = library.NewFunction("bake_sky_irradiance");
            if (skyFunction.NativePtr == 0)
                throw new InvalidOperationException("Metal sky irradiance entry point is missing.");
            try
            {
                NSError skyError = default;
                _skyPipeline = _device.NewComputePipelineState(skyFunction, ref skyError);
                if (_skyPipeline.NativePtr == 0 || skyError.NativePtr != 0)
                    throw new InvalidOperationException($"Metal sky irradiance pipeline creation failed: {Describe(skyError)}");
            }
            finally { skyFunction.Dispose(); }
        }
        finally { library.Dispose(); }
        cancellationToken.ThrowIfCancellationRequested();

        var indices = new uint[checked(triangleCount * 3)];
        var primitiveData = new Primitive[triangleCount];
        int primitiveIndex = 0;
        for (int surfaceIndex = 0; surfaceIndex < surfaces.Length; surfaceIndex++)
        {
            ref readonly Surface surface = ref surfaces[surfaceIndex];
            for (int corner = 1; corner < surface.VertexCount - 1; corner++)
            {
                int offset = primitiveIndex * 3;
                indices[offset] = (uint)surface.VertexOffset;
                indices[offset + 1] = (uint)(surface.VertexOffset + corner);
                indices[offset + 2] = (uint)(surface.VertexOffset + corner + 1);
                primitiveData[primitiveIndex++] = new Primitive(surfaceIndex, corner);
            }
        }

        MTLBuffer indexBuffer = NewSharedBuffer((ulong)indices.Length * sizeof(uint));
        if (indexBuffer.NativePtr == 0)
        {
            FailureReason = "Metal ray triangle index allocation failed.";
            return false;
        }
        try
        {
            indices.AsSpan().CopyTo(new Span<uint>((void*)indexBuffer.Contents, indices.Length));
            _primitives = NewSharedBuffer((ulong)triangleCount * (ulong)sizeof(Primitive));
            _surfaces = NewSharedBuffer((ulong)surfaces.Length * (ulong)sizeof(Surface));
            _vertices = NewSharedBuffer((ulong)vertices.Length * (ulong)sizeof(Vector3));
            for (int slot = 0; slot < 2; slot++)
            {
                _rays[slot] = NewSharedBuffer((ulong)MaximumRayCount * (ulong)sizeof(Ray));
                _headers[slot] = NewSharedBuffer((ulong)MaximumRayCount * sizeof(int));
                _results[slot] = NewSharedBuffer((ulong)MaximumRayCount * 16 * (ulong)sizeof(Hit));
            }
            if (_primitives.NativePtr == 0 || _surfaces.NativePtr == 0 ||
                _vertices.NativePtr == 0 || _rays.Any(buffer => buffer.NativePtr == 0) ||
                _headers.Any(buffer => buffer.NativePtr == 0) ||
                _results.Any(buffer => buffer.NativePtr == 0))
            {
                FailureReason = "Metal ray batch buffer allocation failed.";
                return false;
            }
            primitiveData.AsSpan().CopyTo(new Span<Primitive>((void*)_primitives.Contents, triangleCount));
            surfaces.CopyTo(new Span<Surface>((void*)_surfaces.Contents, surfaces.Length));
            vertices.CopyTo(new Span<Vector3>((void*)_vertices.Contents, vertices.Length));

            using var geometry = new MTLAccelerationStructureTriangleGeometryDescriptor
            {
                VertexBuffer = _vertices,
                VertexStride = (ulong)sizeof(Vector3),
                IndexBuffer = indexBuffer,
                IndexType = MTLIndexType.UInt32,
                TriangleCount = (ulong)triangleCount,
                Opaque = false
            };
            using var descriptor = new MTLPrimitiveAccelerationStructureDescriptor
            {
                GeometryDescriptors = NSArray.Array(new NSObject(geometry.NativePtr))
            };
            MTLAccelerationStructureSizes sizes = _device.AccelerationStructureSizes(descriptor);
            if (sizes.accelerationStructureSize == 0 || sizes.buildScratchBufferSize == 0)
            {
                FailureReason = "Metal ray triangle size calculation failed.";
                return false;
            }
            _acceleration = _device.NewAccelerationStructure(sizes.accelerationStructureSize);
            if (_acceleration.NativePtr == 0)
            {
                FailureReason = "Metal ray acceleration structure allocation failed.";
                return false;
            }
            MTLBuffer scratch = _device.NewBuffer(sizes.buildScratchBufferSize,
                MTLResourceOptions.ResourceStorageModePrivate);
            if (scratch.NativePtr == 0)
            {
                FailureReason = "Metal ray triangle build scratch allocation failed.";
                return false;
            }
            try
            {
                MTLCommandBuffer command = _queue.CommandBuffer();
                if (command.NativePtr == 0)
                {
                    FailureReason = "Metal ray triangle build command allocation failed.";
                    return false;
                }
                MTLAccelerationStructureCommandEncoder encoder =
                    command.AccelerationStructureCommandEncoder();
                if (encoder.NativePtr == 0)
                {
                    FailureReason = "Metal ray triangle build encoder creation failed.";
                    return false;
                }
                encoder.BuildAccelerationStructure(_acceleration, descriptor, scratch, 0);
                encoder.EndEncoding();
                command.Commit();
                command.WaitUntilCompleted();
                cancellationToken.ThrowIfCancellationRequested();
                if (command.Status != MTLCommandBufferStatus.Completed)
                    throw new InvalidOperationException($"Metal ray triangle build failed: {Describe(command.Error)}");
            }
            finally { scratch.Dispose(); }
        }
        finally { indexBuffer.Dispose(); }
        return true;
    }

    public bool TryConfigureSky(ReadOnlySpan<SkyMaterial> materials,
        ReadOnlySpan<SkyVertex> attributes, ReadOnlySpan<byte> images,
        ReadOnlySpan<Vector3> directions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (materials.Length != _surfaceCount || directions.Length != 24 ||
            (long)materials.Length * sizeof(SkyMaterial) > MaximumSceneBytes ||
            (long)attributes.Length * sizeof(SkyVertex) > MaximumSceneBytes ||
            images.Length > MaximumSceneBytes || images.IsEmpty) return false;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            _skyMaterials = NewSharedBuffer((ulong)materials.Length * (ulong)sizeof(SkyMaterial));
            _skyAttributes = NewSharedBuffer((ulong)attributes.Length * (ulong)sizeof(SkyVertex));
            _skyImages = NewSharedBuffer((ulong)images.Length);
            _skyDirections = NewSharedBuffer((ulong)directions.Length * (ulong)sizeof(Vector3));
            _skySamples = NewSharedBuffer((ulong)SkySampleCapacity * (ulong)sizeof(Ray));
            _skyOutput = NewSharedBuffer((ulong)SkySampleCapacity * (ulong)sizeof(Vector3));
            if (_skyMaterials.NativePtr == 0 || _skyAttributes.NativePtr == 0 ||
                _skyImages.NativePtr == 0 || _skyDirections.NativePtr == 0 ||
                _skySamples.NativePtr == 0 || _skyOutput.NativePtr == 0) return false;
            materials.CopyTo(new Span<SkyMaterial>((void*)_skyMaterials.Contents, materials.Length));
            attributes.CopyTo(new Span<SkyVertex>((void*)_skyAttributes.Contents, attributes.Length));
            images.CopyTo(new Span<byte>((void*)_skyImages.Contents, images.Length));
            directions.CopyTo(new Span<Vector3>((void*)_skyDirections.Contents, directions.Length));
            _skyReady = true;
            return true;
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public bool TryBakeSkyIrradiance(ReadOnlySpan<Ray> samples, float ambientScale,
        Span<Vector3> irradiance, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_unavailable || !_skyReady ||
            _submittedBatches != _readBatches || samples.IsEmpty ||
            samples.Length > SkySampleCapacity || irradiance.Length < samples.Length) return false;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            samples.CopyTo(new Span<Ray>((void*)_skySamples.Contents, samples.Length));
            MTLCommandBuffer command = _queue.CommandBuffer();
            if (command.NativePtr == 0) return false;
            MTLComputeCommandEncoder encoder = command.ComputeCommandEncoder();
            if (encoder.NativePtr == 0) return false;
            encoder.SetComputePipelineState(_skyPipeline);
            encoder.SetAccelerationStructure(_acceleration, 0);
            encoder.SetBuffer(_skySamples, 0, 1);
            encoder.SetBuffer(_skyOutput, 0, 2);
            encoder.SetBuffer(_primitives, 0, 3);
            uint sampleCount = (uint)samples.Length;
            encoder.SetBytes((nint)(&sampleCount), sizeof(uint), 4);
            encoder.SetBuffer(_surfaces, 0, 5);
            encoder.SetBuffer(_skyMaterials, 0, 6);
            encoder.SetBuffer(_skyAttributes, 0, 7);
            encoder.SetBuffer(_skyImages, 0, 8);
            encoder.SetBuffer(_skyDirections, 0, 9);
            encoder.SetBytes((nint)(&ambientScale), sizeof(float), 10);
            uint worldSurfaceCount = (uint)_worldSurfaceCount;
            encoder.SetBytes((nint)(&worldSurfaceCount), sizeof(uint), 11);
            encoder.DispatchThreads(new MTLSize { width = sampleCount, height = 1, depth = 1 },
                new MTLSize { width = 64, height = 1, depth = 1 });
            encoder.EndEncoding();
            command.Commit();
            command.WaitUntilCompleted();
            cancellationToken.ThrowIfCancellationRequested();
            if (command.Status != MTLCommandBufferStatus.Completed)
            {
                FailureReason = $"Metal sky irradiance command failed: {Describe(command.Error)}";
                _unavailable = true;
                return false;
            }
            new ReadOnlySpan<Vector3>((void*)_skyOutput.Contents, samples.Length).CopyTo(irradiance);
            return true;
        }
        catch (InvalidOperationException exception)
        {
            FailureReason = $"Metal sky irradiance submission failed: {exception.Message}";
            _unavailable = true;
            return false;
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public override bool TrySubmit(ReadOnlySpan<Ray> rays, bool shadow, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_submittedBatches - _readBatches >= MaximumInFlightBatches)
            throw new InvalidOperationException("Read a pending GPU ray batch before submitting another.");
        if (rays.Length > MaximumRayCount)
            throw new ArgumentException("The GPU ray batch exceeds its capacity.", nameof(rays));
        if (rays.IsEmpty || _unavailable) return false;
        foreach (ref readonly Ray ray in rays)
        {
            if (!SupportedCoordinate(ray.Origin) ||
                !SupportedDirection(ray.Direction) || ray.Direction == Vector3.Zero)
                return false;
        }

        int submittedBefore = _submittedBatches;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            int slot = _submittedBatches % MaximumInFlightBatches;
            rays.CopyTo(new Span<Ray>((void*)_rays[slot].Contents, rays.Length));
            MTLCommandBuffer command = _queue.CommandBuffer();
            if (command.NativePtr == 0)
            {
                FailureReason = "Metal ray command-buffer allocation failed.";
                _unavailable = true;
                return false;
            }
            MTLComputeCommandEncoder encoder = command.ComputeCommandEncoder();
            if (encoder.NativePtr == 0)
            {
                FailureReason = "Metal ray compute encoder creation failed.";
                _unavailable = true;
                return false;
            }
            encoder.SetComputePipelineState(_pipeline);
            encoder.SetAccelerationStructure(_acceleration, 0);
            encoder.SetBuffer(_rays[slot], 0, 1);
            encoder.SetBuffer(_results[slot], 0, 2);
            encoder.SetBuffer(_primitives, 0, 3);
            uint rayCount = (uint)rays.Length;
            encoder.SetBytes((nint)(&rayCount), sizeof(uint), 4);
            encoder.SetBuffer(_surfaces, 0, 5);
            uint shadowMode = shadow ? 1u : 0u;
            encoder.SetBytes((nint)(&shadowMode), sizeof(uint), 6);
            encoder.SetBuffer(_headers[slot], 0, 7);
            uint worldSurfaceCount = (uint)_worldSurfaceCount;
            encoder.SetBytes((nint)(&worldSurfaceCount), sizeof(uint), 8);
            uint alphaReady = _skyReady ? 1u : 0u;
            encoder.SetBuffer(alphaReady != 0 ? _skyMaterials : _surfaces, 0, 9);
            encoder.SetBuffer(alphaReady != 0 ? _skyAttributes : _vertices, 0, 10);
            encoder.SetBuffer(alphaReady != 0 ? _skyImages : _results[slot], 0, 11);
            encoder.SetBytes((nint)(&alphaReady), sizeof(uint), 12);
            encoder.DispatchThreads(
                new MTLSize { width = rayCount, height = 1, depth = 1 },
                new MTLSize { width = 64, height = 1, depth = 1 });
            encoder.EndEncoding();
            // commandBuffer is autoreleased. Retain it across pool drainage and
            // across the caller's CPU work until readback completes.
            MetalObjectiveC.Retain(command.NativePtr);
            _pending[slot] = command;
            _pendingRayCounts[slot] = rays.Length;
            _submittedBatches++;
            command.Commit();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            FailureReason = $"Metal ray submission failed: {exception.Message}";
            _unavailable = true;
            if (_submittedBatches > submittedBefore) ClearNewest(wait: true);
            return false;
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public override bool TryRead(Span<Hit> results, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_submittedBatches == _readBatches)
            throw new InvalidOperationException("No GPU ray batch is pending.");
        int slot = _readBatches % MaximumInFlightBatches;
        int resultCount = _pendingRayCounts[slot] * ResultStride;
        if (results.Length < resultCount)
            throw new ArgumentException("The GPU ray result buffer is too small.", nameof(results));
        nint pool = objc_autoreleasePoolPush();
        try
        {
            _pending[slot].WaitUntilCompleted();
            cancellationToken.ThrowIfCancellationRequested();
            if (_pending[slot].Status != MTLCommandBufferStatus.Completed)
            {
                FailureReason = $"Metal ray command failed: {Describe(_pending[slot].Error)}";
                _unavailable = true;
                return false;
            }
            var headers = new ReadOnlySpan<int>((void*)_headers[slot].Contents, _pendingRayCounts[slot]);
            var hits = new ReadOnlySpan<Hit>((void*)_results[slot].Contents, _pendingRayCounts[slot] * 16);
            for (int ray = 0; ray < _pendingRayCounts[slot]; ray++)
            {
                int count = headers[ray];
                int row = ray * ResultStride;
                results[row] = new Hit(count, 0);
                if (count > 0 && count <= 16)
                    hits.Slice(ray * 16, count).CopyTo(results.Slice(row + 1, count));
            }
            return true;
        }
        catch (InvalidOperationException exception)
        {
            FailureReason = $"Metal ray readback failed: {exception.Message}";
            _unavailable = true;
            return false;
        }
        finally
        {
            ClearOldest(wait: true);
            objc_autoreleasePoolPop(pool);
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!OperatingSystem.IsMacOS()) return;
        nint pool = objc_autoreleasePoolPush();
        try
        {
            ClearPending(wait: true);
            if (_acceleration.NativePtr != 0) _acceleration.Dispose();
            if (_primitives.NativePtr != 0) _primitives.Dispose();
            if (_surfaces.NativePtr != 0) _surfaces.Dispose();
            if (_vertices.NativePtr != 0) _vertices.Dispose();
            if (_skyMaterials.NativePtr != 0) _skyMaterials.Dispose();
            if (_skyAttributes.NativePtr != 0) _skyAttributes.Dispose();
            if (_skyImages.NativePtr != 0) _skyImages.Dispose();
            if (_skyDirections.NativePtr != 0) _skyDirections.Dispose();
            if (_skySamples.NativePtr != 0) _skySamples.Dispose();
            if (_skyOutput.NativePtr != 0) _skyOutput.Dispose();
            foreach (MTLBuffer buffer in _rays) if (buffer.NativePtr != 0) buffer.Dispose();
            foreach (MTLBuffer buffer in _headers) if (buffer.NativePtr != 0) buffer.Dispose();
            foreach (MTLBuffer buffer in _results) if (buffer.NativePtr != 0) buffer.Dispose();
            if (_pipeline.NativePtr != 0) _pipeline.Dispose();
            if (_skyPipeline.NativePtr != 0) _skyPipeline.Dispose();
            if (_queue.NativePtr != 0) _queue.Dispose();
            if (_device.NativePtr != 0) _device.Dispose();
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    private MTLBuffer NewSharedBuffer(ulong length)
    {
        MTLBuffer buffer = _device.NewBuffer(length, MTLResourceOptions.ResourceStorageModeShared);
        if (buffer.NativePtr != 0 && buffer.Contents == 0)
        {
            buffer.Dispose();
            return default;
        }
        return buffer;
    }

    private void ClearPending(bool wait)
    {
        while (_readBatches < _submittedBatches) ClearOldest(wait);
    }

    private void ClearOldest(bool wait)
    {
        int slot = _readBatches % MaximumInFlightBatches;
        MTLCommandBuffer command = _pending[slot];
        if (command.NativePtr == 0) { _readBatches++; return; }
        try
        {
            if (wait && command.Status is MTLCommandBufferStatus.Committed or MTLCommandBufferStatus.Scheduled)
                command.WaitUntilCompleted();
        }
        finally
        {
            MetalObjectiveC.Release(command.NativePtr);
            _pending[slot] = default;
            _pendingRayCounts[slot] = 0;
            _readBatches++;
        }
    }

    private void ClearNewest(bool wait)
    {
        int slot = (_submittedBatches - 1) % MaximumInFlightBatches;
        MTLCommandBuffer command = _pending[slot];
        try
        {
            if (command.NativePtr != 0 && wait &&
                command.Status is MTLCommandBufferStatus.Committed or MTLCommandBufferStatus.Scheduled)
                command.WaitUntilCompleted();
        }
        finally
        {
            if (command.NativePtr != 0) MetalObjectiveC.Release(command.NativePtr);
            _pending[slot] = default;
            _pendingRayCounts[slot] = 0;
            _submittedBatches--;
        }
    }

    private static string Describe(NSError error) =>
        error.NativePtr == 0 ? "unknown Metal error" :
        error.LocalizedDescription.ToString() ?? "unknown Metal error";

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_autoreleasePoolPop(nint pool);
}
