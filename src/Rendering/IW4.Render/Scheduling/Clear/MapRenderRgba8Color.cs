namespace IW4.Render.Scheduling.Clear;

/// <summary>Four byte RGBA value in the order read from a PS3 color dvar.</summary>
public readonly record struct MapRenderRgba8Color(
    byte Red,
    byte Green,
    byte Blue,
    byte Alpha);
