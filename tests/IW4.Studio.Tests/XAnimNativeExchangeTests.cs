using System.Text.Json;
using IW4.Formats.SourceFormat.XAnim;
using IW4.Game.Assets.XAnim;
using IW4.Game.Pointers;
using Xunit;

namespace IW4.Studio.Tests;

public sealed class XAnimNativeExchangeTests
{
    private static readonly XAnimNativeExchange Exchange = new();

    // Independent v1 source fixture: bytes are base64, while shorts, ints and indices are numbers.
    private const string KnownSource = """
        {
          "format":"iw4-ps3-xanim","version":1,"name":"sample",
          "dataByteCount":2,"dataShortCount":2,"dataIntCount":1,
          "randomDataByteCount":1,"randomDataIntCount":1,"randomDataShortCount":2,
          "indexCount":2,"numFrames":256,"flags":5,"deltaFlags":9,
          "boneCounts":"AQIDBAUGBwgJCg==","boneNameCount":2,"notifyCount":1,
          "assetType":3,"pad1F":171,"framerate":30,"frequency":0.5,
          "names":["root","weapon"],"notify":[{"name":"fire","time":0.25}],
          "packedDataStreams":{
            "quantizedBytes":"AP8=","quantizedShorts":[-2,32767],"quantizedInts":[-2147483648],
            "randomizedQuantizedShorts":[-32768,12],"randomizedQuantizedBytes":"gA==",
            "randomizedQuantizedInts":[2147483647]
          },
          "indices":[0,255],"deltaPart":null
        }
        """;

    [Fact]
    public void Link_reads_known_v1_metadata_and_native_stream_values()
    {
        using TempSource source = new();
        source.Write(KnownSource);

        XAnimPartsAsset asset = Exchange.Link(source.Directory, "sample");

        Assert.Equal("sample", asset.Name);
        Assert.Equal((ushort)2, asset.DataByteCount);
        Assert.Equal((ushort)2, asset.DataShortCount);
        Assert.Equal((ushort)1, asset.DataIntCount);
        Assert.Equal((ushort)1, asset.RandomDataByteCount);
        Assert.Equal((ushort)1, asset.RandomDataIntCount);
        Assert.Equal(2, asset.RandomDataShortCount);
        Assert.Equal(2, asset.IndexCount);
        Assert.Equal((ushort)256, asset.NumFrames);
        Assert.Equal((byte)5, asset.Flags);
        Assert.Equal((byte)9, asset.DeltaFlags);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (byte)i), asset.BoneCounts);
        Assert.Equal((byte)2, asset.BoneNameCount);
        Assert.Equal((byte)1, asset.NotifyCount);
        Assert.Equal((byte)3, asset.AssetType);
        Assert.Equal((byte)171, asset.Pad1F);
        Assert.Equal(30f, asset.Framerate);
        Assert.Equal(0.5f, asset.Frequency);
        Assert.Equal(["root", "weapon"], asset.Names.Select(name => name.Text));
        Assert.Equal("fire", Assert.Single(asset.Notify).Name.Text);
        Assert.Equal(0.25f, Assert.Single(asset.Notify).Time);
        Assert.Equal(new byte[] { 0, 255 }, asset.PackedDataStreams.QuantizedBytes);
        Assert.Equal(new short[] { -2, 32767 }, asset.PackedDataStreams.QuantizedShorts);
        Assert.Equal(new[] { int.MinValue }, asset.PackedDataStreams.QuantizedInts);
        Assert.Equal(new short[] { short.MinValue, 12 }, asset.PackedDataStreams.RandomizedQuantizedShorts);
        Assert.Equal(new byte[] { 128 }, asset.PackedDataStreams.RandomizedQuantizedBytes);
        Assert.Equal(new[] { int.MaxValue }, asset.PackedDataStreams.RandomizedQuantizedInts);
        Assert.Equal(new ushort[] { 0, 255 }, asset.Indices.FrameIndices);
        Assert.Null(asset.DeltaPart);
    }

    [Fact]
    public void Unlink_writes_v1_fields_in_known_order_and_preserves_native_array_encodings()
    {
        using TempSource source = new();
        source.Write(KnownSource);
        XAnimPartsAsset asset = Exchange.Link(source.Directory, "sample");
        IReadOnlyList<string> paths = Exchange.Unlink(source.Directory, asset);
        string path = Path.Combine(source.Directory, "xanim_native", "sample.json");

        Assert.Equal(path, Assert.Single(paths));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = json.RootElement;
        Assert.Equal(new[]
        {
            "format", "version", "name", "dataByteCount", "dataShortCount", "dataIntCount",
            "randomDataByteCount", "randomDataIntCount", "randomDataShortCount", "indexCount",
            "numFrames", "flags", "deltaFlags", "boneCounts", "boneNameCount", "notifyCount",
            "assetType", "pad1F", "framerate", "frequency", "names", "notify",
            "packedDataStreams", "indices", "deltaPart"
        }, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal("iw4-ps3-xanim", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("AQIDBAUGBwgJCg==", root.GetProperty("boneCounts").GetString());
        Assert.Equal("root", root.GetProperty("names")[0].GetString());
        Assert.Equal("fire", root.GetProperty("notify")[0].GetProperty("name").GetString());
        JsonElement streams = root.GetProperty("packedDataStreams");
        Assert.Equal(new[]
        {
            "quantizedBytes", "quantizedShorts", "quantizedInts", "randomizedQuantizedShorts",
            "randomizedQuantizedBytes", "randomizedQuantizedInts"
        }, streams.EnumerateObject().Select(property => property.Name));
        Assert.Equal("AP8=", streams.GetProperty("quantizedBytes").GetString());
        Assert.Equal(-2, streams.GetProperty("quantizedShorts")[0].GetInt16());
        Assert.Equal(int.MinValue, streams.GetProperty("quantizedInts")[0].GetInt32());
        Assert.Equal("gA==", streams.GetProperty("randomizedQuantizedBytes").GetString());
        Assert.Equal(255, root.GetProperty("indices")[1].GetUInt16());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("deltaPart").ValueKind);
    }

    [Theory]
    [InlineData("trans", "{\"size\":0,\"smallTrans\":7,\"pad3\":19,\"frame0\":{\"x\":1,\"y\":2,\"z\":3},\"frames\":null}")]
    [InlineData("quat2", "{\"size\":0,\"pad2\":11,\"pad3\":12,\"frame0\":{\"value0\":-4,\"value1\":5},\"frames\":null}")]
    [InlineData("quat", "{\"size\":0,\"pad2\":13,\"pad3\":14,\"frame0\":{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4},\"frames\":null}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":7,\"pad3\":19,\"frame0\":null,\"frames\":{\"mins\":{\"x\":1,\"y\":2,\"z\":3},\"size\":{\"x\":4,\"y\":5,\"z\":6},\"indices\":[0,255],\"smallFrames\":[{\"x\":1,\"y\":2,\"z\":3},{\"x\":254,\"y\":255,\"z\":0}],\"largeFrames\":null}}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":0,\"pad3\":19,\"frame0\":null,\"frames\":{\"mins\":{\"x\":1,\"y\":2,\"z\":3},\"size\":{\"x\":4,\"y\":5,\"z\":6},\"indices\":[0,255],\"smallFrames\":null,\"largeFrames\":[{\"x\":-32768,\"y\":2,\"z\":3},{\"x\":32767,\"y\":-5,\"z\":6}]}}")]
    [InlineData("quat2", "{\"size\":1,\"pad2\":11,\"pad3\":12,\"frame0\":null,\"frames\":{\"indices\":[0,255],\"values\":[{\"value0\":-4,\"value1\":5},{\"value0\":6,\"value1\":-7}]}}")]
    [InlineData("quat", "{\"size\":1,\"pad2\":13,\"pad3\":14,\"frame0\":null,\"frames\":{\"indices\":[0,255],\"values\":[{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4},{\"value0\":-5,\"value1\":6,\"value2\":-7,\"value3\":8}]}}")]
    public void Delta_branches_link_and_unlink_with_known_values(string branch, string payload)
    {
        using TempSource source = new();
        source.Write(WithDelta(branch, payload));

        XAnimPartsAsset asset = Exchange.Link(source.Directory, "sample");
        Assert.NotNull(asset.DeltaPart);
        Exchange.Unlink(source.Directory, asset);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(source.Path));
        JsonElement delta = json.RootElement.GetProperty("deltaPart");
        JsonElement actual = delta.GetProperty(branch);
        using JsonDocument expected = JsonDocument.Parse(payload);
        AssertContains(expected.RootElement, actual);
        foreach (string other in new[] { "trans", "quat2", "quat" }.Where(name => name != branch))
            Assert.Equal(JsonValueKind.Null, delta.GetProperty(other).ValueKind);

        if (branch == "trans")
        {
            Assert.Equal((byte)(payload.Contains("\"smallTrans\":7") ? 7 : 0), asset.DeltaPart.Trans!.SmallTrans);
            Assert.Equal((byte)19, asset.DeltaPart.Trans!.Pad3);
            if (asset.DeltaPart.Trans.Size == 0)
                Assert.Equal(new XAnimPartTransFrame0(1, 2, 3), asset.DeltaPart.Trans.Frame0);
            else
            {
                Assert.Equal(new ushort[] { 0, 255 }, asset.DeltaPart.Trans.Frames!.DynamicFrames.FrameIndices);
                Assert.Equal(new XAnimVec3(1, 2, 3), asset.DeltaPart.Trans.Frames.Mins);
                Assert.Equal(new XAnimVec3(4, 5, 6), asset.DeltaPart.Trans.Frames.Size);
                if (asset.DeltaPart.Trans.SmallTrans != 0)
                    Assert.Equal(new SmallXAnimTransFrame(254, 255, 0),
                        Assert.IsType<SmallXAnimTransFramePayload>(asset.DeltaPart.Trans.Frames.FramePayload).Frames[1]);
                else
                    Assert.Equal(new LargeXAnimTransFrame(32767, -5, 6),
                        Assert.IsType<LargeXAnimTransFramePayload>(asset.DeltaPart.Trans.Frames.FramePayload).Frames[1]);
            }
        }
        else if (branch == "quat2")
        {
            Assert.Equal((byte)11, asset.DeltaPart.Quat2!.Pad2);
            Assert.Equal((byte)12, asset.DeltaPart.Quat2.Pad3);
            Assert.Equal(asset.DeltaPart.Quat2.Size == 0 ? new XQuat2(-4, 5) : null, asset.DeltaPart.Quat2.Frame0);
            if (asset.DeltaPart.Quat2.Size != 0)
            {
                Assert.Equal(new ushort[] { 0, 255 }, asset.DeltaPart.Quat2.Frames!.DynamicFrames.FrameIndices);
                Assert.Equal(new XQuat2(6, -7), asset.DeltaPart.Quat2.Frames.Frames[1]);
            }
        }
        else
        {
            Assert.Equal((byte)13, asset.DeltaPart.Quat!.Pad2);
            Assert.Equal((byte)14, asset.DeltaPart.Quat.Pad3);
            Assert.Equal(asset.DeltaPart.Quat.Size == 0 ? new XQuat(1, 2, 3, 4) : null, asset.DeltaPart.Quat.Frame0);
            if (asset.DeltaPart.Quat.Size != 0)
            {
                Assert.Equal(new ushort[] { 0, 255 }, asset.DeltaPart.Quat.Frames!.DynamicFrames.FrameIndices);
                Assert.Equal(new XQuat(-5, 6, -7, 8), asset.DeltaPart.Quat.Frames.Frames[1]);
            }
        }
    }

    [Theory]
    [InlineData(255, 255, true)]
    [InlineData(256, 255, true)]
    [InlineData(256, 256, true)]
    [InlineData(255, 256, false)]
    public void Native_index_width_boundary_follows_numFrames(int numFrames, int index, bool accepted)
    {
        using TempSource source = new();
        string json = KnownSource.Replace("\"numFrames\":256", $"\"numFrames\":{numFrames}")
            .Replace("\"indices\":[0,255]", $"\"indices\":[0,{index}]");
        source.Write(json);

        if (accepted)
            Assert.Equal((ushort)index, Exchange.Link(source.Directory, "sample").Indices.FrameIndices[1]);
        else
            Assert.Throws<InvalidDataException>(() => Exchange.Link(source.Directory, "sample"));
    }

    [Theory]
    [InlineData("\"format\":\"iw4-ps3-xanim\"", "\"format\":\"other\"")]
    [InlineData("\"version\":1", "\"version\":2")]
    [InlineData("\"name\":\"sample\"", "\"name\":\"other\"")]
    [InlineData("\"dataByteCount\":2", "\"dataByteCount\":3")]
    [InlineData("\"boneNameCount\":2", "\"boneNameCount\":1")]
    [InlineData("\"indexCount\":2", "\"indexCount\":3")]
    [InlineData("\"boneCounts\":\"AQIDBAUGBwgJCg==\"", "\"boneCounts\":\"AQID\"")]
    [InlineData("\"quantizedBytes\":\"AP8=\"", "\"quantizedBytes\":null")]
    [InlineData("\"names\":[\"root\",\"weapon\"]", "\"names\":[\"root\",null]")]
    [InlineData("\"notify\":[{\"name\":\"fire\",\"time\":0.25}]", "\"notify\":[null]")]
    public void Link_rejects_invalid_identity_or_incomplete_native_arrays(string original, string replacement)
    {
        using TempSource source = new();
        source.Write(KnownSource.Replace(original, replacement));
        Assert.Throws<InvalidDataException>(() => Exchange.Link(source.Directory, "sample"));
    }

    [Theory]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":7,\"frame0\":null,\"frames\":{\"mins\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"indices\":[0,1],\"smallFrames\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":1}],\"largeFrames\":[]}}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":0,\"frame0\":null,\"frames\":{\"mins\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"indices\":[0,1],\"smallFrames\":[],\"largeFrames\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":1}]}}")]
    [InlineData("trans", "{\"size\":0,\"frame0\":{\"x\":0,\"y\":0,\"z\":0},\"frames\":{}}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":1,\"frame0\":null,\"frames\":null}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":1,\"frame0\":null,\"frames\":{\"mins\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"indices\":[0],\"smallFrames\":[{\"x\":0,\"y\":0,\"z\":0},{\"x\":1,\"y\":1,\"z\":1}]}}")]
    [InlineData("trans", "{\"size\":1,\"smallTrans\":1,\"frame0\":null,\"frames\":{\"mins\":{\"x\":0,\"y\":0,\"z\":0},\"size\":{\"x\":1,\"y\":1,\"z\":1},\"indices\":[0,1],\"smallFrames\":[null,{\"x\":1,\"y\":1,\"z\":1}]}}")]
    [InlineData("quat2", "{\"size\":0,\"frame0\":{\"value0\":1,\"value1\":2},\"frames\":{}}")]
    [InlineData("quat2", "{\"size\":1,\"frame0\":null,\"frames\":{\"indices\":[0,1],\"values\":[{\"value0\":1,\"value1\":2}]}}")]
    [InlineData("quat", "{\"size\":1,\"frame0\":null,\"frames\":{\"indices\":[0,1],\"values\":[null,{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4}]}}")]
    [InlineData("quat", "{\"size\":1,\"frame0\":{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4},\"frames\":{\"indices\":[0,1],\"values\":[{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4},{\"value0\":1,\"value1\":2,\"value2\":3,\"value3\":4}]}}")]
    public void Link_rejects_incomplete_or_conflicting_delta_payloads(string branch, string payload)
    {
        using TempSource source = new();
        source.Write(WithDelta(branch, payload));
        Assert.Throws<InvalidDataException>(() => Exchange.Link(source.Directory, "sample"));
    }

    [Fact]
    public void Unlink_rejects_unmaterialized_delta_pointer()
    {
        using TempSource source = new();
        Assert.Throws<InvalidDataException>(() => Exchange.Unlink(source.Directory,
            EmptyAsset(deltaPointer: new XPointer<XAnimDeltaPart>(-1))));
    }

    [Fact]
    public void Unlink_rejects_null_stream_with_negative_signed_count_as_invalid_data()
    {
        using TempSource source = new();
        Assert.Throws<InvalidDataException>(() => Exchange.Unlink(source.Directory,
            EmptyAsset(randomShortCount: -1, streams: new XAnimPackedDataStreams
            {
                RandomizedQuantizedShorts = null!
            })));
    }

    private static string WithDelta(string branch, string payload) =>
        KnownSource.Replace("\"deltaPart\":null",
            $"\"deltaPart\":{{\"trans\":{(branch == "trans" ? payload : "null")}," +
            $"\"quat2\":{(branch == "quat2" ? payload : "null")}," +
            $"\"quat\":{(branch == "quat" ? payload : "null")}}}");

    private static void AssertContains(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in expected.EnumerateObject())
                AssertContains(property.Value, actual.GetProperty(property.Name));
        }
        else if (expected.ValueKind == JsonValueKind.Array)
        {
            Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
            for (int i = 0; i < expected.GetArrayLength(); i++)
                AssertContains(expected[i], actual[i]);
        }
        else
        {
            Assert.True(JsonElement.DeepEquals(expected, actual));
        }
    }

    private static XAnimPartsAsset EmptyAsset(int randomShortCount = 0,
        XAnimPackedDataStreams? streams = null, XPointer<XAnimDeltaPart> deltaPointer = default) => new()
    {
        Name = "sample", BoneCounts = new byte[10], PackedDataStreams = streams ?? new(),
        RandomDataShortCount = randomShortCount, DeltaPartPointer = deltaPointer
    };

    private sealed class TempSource : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "IW4-XAnimNative-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "xanim_native", "sample.json");

        public void Write(string json)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, json);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
