using IW4.Render.Scheduling.Fog;

namespace IW4.Render.Scheduling.Clear;

/// <summary>
/// Operational inputs read by one PS3 <c>R_GetClearColor</c> invocation.
/// No preview, diagnostic, or captured value is implied by this type.
/// </summary>
public sealed class MapRenderNormalCameraClearColorInput
{
    public MapRenderNormalCameraClearColorInput(
        MapRenderNormalCameraClearMode mode,
        bool developerEnabled,
        int systemMilliseconds,
        MapRenderRgba8Color primaryColor,
        MapRenderRgba8Color secondaryColor,
        MapRenderNormalCameraFarPlaneState farPlane,
        MapRenderActiveFogState fog)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        ArgumentNullException.ThrowIfNull(farPlane);
        ArgumentNullException.ThrowIfNull(fog);

        Mode = mode;
        DeveloperEnabled = developerEnabled;
        SystemMilliseconds = systemMilliseconds;
        PrimaryColor = primaryColor;
        SecondaryColor = secondaryColor;
        FarPlane = farPlane;
        Fog = fog;
    }

    public MapRenderNormalCameraClearMode Mode { get; }

    public bool DeveloperEnabled { get; }

    public int SystemMilliseconds { get; }

    public MapRenderRgba8Color PrimaryColor { get; }

    public MapRenderRgba8Color SecondaryColor { get; }

    public MapRenderNormalCameraFarPlaneState FarPlane { get; }

    public MapRenderActiveFogState Fog { get; }
}
