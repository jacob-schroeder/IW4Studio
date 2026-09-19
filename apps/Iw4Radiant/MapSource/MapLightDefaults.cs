namespace Iw4Radiant.MapSource;

// Radiant light defaults from COD4 SDK bin/cod4.def.
internal static class MapLightDefaults
{
    internal const string Definition = "light_point_linear";
    internal const float Radius = 200;
    internal const float Intensity = 1;
    internal const string Color = "1 1 1";
    internal const float InnerFov = 0;
    internal const float Exponent = 0;
    internal const float TargetRadius = 64;
    internal const int PrimaryOmni = 1;
    internal const int PrimarySpot = 2;
}
