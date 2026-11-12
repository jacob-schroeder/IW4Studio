using System.Reflection;
using IW4.Formats.SourceFormat.XAnim;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class XAnimModelTests
{
    // Exercise the internal construction boundary without adding a test-only production API.
    private static readonly Type KeyFramesType = Assert.IsAssignableFrom<Type>(
        typeof(XAnimPlaybackClip).Assembly.GetType("IW4.Formats.SourceFormat.XAnim.XAnimKeyFrames`1"))
        .MakeGenericType(typeof(int));

    public static IEnumerable<object[]> InvalidTracks =>
    [
        new object[] { Array.Empty<ushort>(), Array.Empty<int>(), (ushort)4 },
        new object[] { new ushort[] { 0 }, new[] { 10 }, (ushort)4 },
        new object[] { new ushort[] { 0, 1 }, new[] { 10 }, (ushort)4 },
        new object[] { new ushort[] { 0, 1 }, new[] { 10, 20, 30 }, (ushort)4 },
        new object[] { new ushort[] { 1, 1 }, new[] { 10, 20 }, (ushort)4 },
        new object[] { new ushort[] { 2, 1 }, new[] { 10, 20 }, (ushort)4 },
        new object[] { new ushort[] { 0, 4 }, new[] { 10, 20 }, (ushort)4 },
        new object[] { new ushort[] { 0, 1 }, new[] { 10, 20 }, (ushort)0 }
    ];

    [Theory]
    [MemberData(nameof(InvalidTracks))]
    public void Animated_track_rejects_invalid_counts_order_and_frame_bounds(
        ushort[] indices, int[] values, ushort loopFrames)
    {
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => Create(indices, values, loopFrames));

        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    [Fact]
    public void Animated_track_owns_immutable_copies_of_indices_and_values()
    {
        ushort[] indices = [0, 3];
        int[] values = [10, 40];
        object track = Create(indices, values, 4);
        indices[0] = 3;
        values[0] = -1;

        var ownedIndices = Assert.IsAssignableFrom<IReadOnlyList<ushort>>(
            ReadProperty(track, "Indices"));
        var ownedValues = Assert.IsAssignableFrom<IReadOnlyList<int>>(
            ReadProperty(track, "Values"));

        Assert.Equal(new ushort[] { 0, 3 }, ownedIndices);
        Assert.Equal(new[] { 10, 40 }, ownedValues);
        Assert.Equal(2, ReadProperty(track, "Count"));
        Assert.Throws<NotSupportedException>(() => ((IList<ushort>)ownedIndices)[0] = 2);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)ownedValues)[0] = 99);
    }

    private static object? ReadProperty(object track, string name) =>
        Assert.IsAssignableFrom<PropertyInfo>(KeyFramesType.GetProperty(
            name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).GetValue(track);

    private static object Create(ushort[] indices, int[] values, ushort loopFrames) =>
        Assert.IsAssignableFrom<object>(Assert.IsAssignableFrom<MethodInfo>(
            KeyFramesType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Invoke(null, [indices, values, loopFrames, "test track"]));
}
