namespace IW4.Assets.Assets.GameMap;

public sealed class VehicleTrackObstacle
{
    public const int SerializedSize = 0x0C;

    public IReadOnlyList<float> Origin { get; init; } = []; // 0x00, vec2
    public float Radius { get; init; }                      // 0x08
}
