using System.Runtime.CompilerServices;
using System.Text.Json;
using IW4.Game.Math;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class VectorAliasTests
{
    [Fact]
    public void Vec2_knots_share_named_and_indexed_components_and_keep_the_weapon_json_names()
    {
        Assert.Equal(8, Unsafe.SizeOf<Vec2>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Vec2>());
        var vector = new Vec2(1, 2);
        vector.X = 3;
        vector.V[1] = 4;
        Assert.Equal(3f, vector.a);
        Assert.Equal(4f, vector.Y);
        vector.b = 5;
        Assert.Equal(5f, vector.V[1]);

        var options = new JsonSerializerOptions { IncludeFields = true };
        Assert.Equal("""{"a":3,"b":5}""", JsonSerializer.Serialize(vector, options));
        Assert.Equal(vector, JsonSerializer.Deserialize<Vec2>("""{"a":3,"b":5}""", options));
    }

    [Fact]
    public void Vec3_copies_are_independent_and_dictionary_keys_use_all_three_components()
    {
        Assert.Equal(12, Unsafe.SizeOf<Vec3>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Vec3>());
        var vector = new Vec3(1, 2, 3);
        var copy = vector;
        copy.V[0] = -0f;
        copy.Z = 7;
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(copy.X));
        Assert.Equal(7f, copy.V[2]);
        Assert.Equal(2f, copy.V[1]);
        Assert.Equal(1f, vector.X);
        Assert.Equal(3f, vector.Z);

        var keys = new HashSet<Vec3> { copy, new Vec3(0, 2, 7), new Vec3(0, 2, 8) };
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void Vec4_exposes_xyzw_and_rgba_over_the_same_four_floats()
    {
        Assert.Equal(16, Unsafe.SizeOf<Vec4>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Vec4>());
        var vector = new Vec4(1, 2, 3, 4);
        Assert.Equal((1f, 2f, 3f, 4f), (vector.R, vector.G, vector.B, vector.A));
        vector.A = 5;
        vector.V[1] = 6;
        vector.Z = 7;
        Assert.Equal((1f, 6f, 7f, 5f), (vector.V[0], vector.G, vector.B, vector.W));
        Assert.Equal(vector, new Vec4(1, 6, 7, 5));
    }
}
