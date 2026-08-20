using System.Globalization;
using System.Numerics;
using IW4.Render;
using IW4.Render.Picking;
using IW4.Render.Transforms;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace IW4.Studio.Desktop.Views;

internal interface INativeMapRenderWindow : IDisposable
{
    event EventHandler<Exception>? Failed;

    event EventHandler? Stopped;

    void Show();
}

/// <summary>
/// Owns the backend-independent input and picking state shared by the native
/// OpenGL and Metal map windows.
/// </summary>
internal sealed class NativeMapRenderInteraction : IDisposable
{
    private readonly MapRenderScene _scene;
    private readonly Func<string, Task>? _copyTextAsync;
    private IInputContext? _input;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    private Vector2 _lastMousePosition;
    private bool _wasDragging;
    private bool _wasPickKeyPressed;
    private MapRenderPickHit? _selectedPick;
    private IReadOnlyList<MapRenderPickCandidate> _selectedPickCandidates = [];
    private IReadOnlyList<MapRenderPickCandidate> _selectedNeighborCandidates = [];
    private bool _lastPickMiss;

    internal NativeMapRenderInteraction(
        MapRenderScene scene,
        RenderCamera initialCamera,
        Func<string, Task>? copyTextAsync)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Camera = initialCamera;
        _copyTextAsync = copyTextAsync;
    }

    internal RenderCamera Camera { get; private set; }

    internal void Initialize(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_input is not null)
        {
            throw new InvalidOperationException(
                "The native map input context is already initialized.");
        }

        _input = window.CreateInput();
        _keyboard = _input.Keyboards.FirstOrDefault();
        _mouse = _input.Mice.FirstOrDefault();
    }

    internal bool Update(IWindow window, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_keyboard is null)
            return false;

        if (_keyboard.IsKeyPressed(Key.Escape))
            return true;

        UpdateMouseLook();
        ApplyPicking(window);

        float delta =
            MapRenderCameraUpdateTiming.ClampElapsedSeconds(elapsedSeconds);
        float yaw = Camera.YawRadians;
        if (_keyboard.IsKeyPressed(Key.Left))
            yaw -= 1.6f * delta;
        if (_keyboard.IsKeyPressed(Key.Right))
            yaw += 1.6f * delta;

        Vector3 movement = Vector3.Zero;
        if (_keyboard.IsKeyPressed(Key.W))
            movement += Camera.Forward;
        if (_keyboard.IsKeyPressed(Key.S))
            movement -= Camera.Forward;
        if (_keyboard.IsKeyPressed(Key.D))
            movement += Camera.Right;
        if (_keyboard.IsKeyPressed(Key.A))
            movement -= Camera.Right;
        if (_keyboard.IsKeyPressed(Key.Up))
            movement += Vector3.UnitY;
        if (_keyboard.IsKeyPressed(Key.Down))
            movement -= Vector3.UnitY;

        float moveSpeed = _keyboard.IsKeyPressed(Key.ShiftLeft)
            ? 2200f
            : 700f;
        Camera = Camera with
        {
            YawRadians = yaw,
            Position = movement == Vector3.Zero
                ? Camera.Position
                : Camera.Position +
                  Vector3.Normalize(movement) * moveSpeed * delta
        };
        return false;
    }

    private void UpdateMouseLook()
    {
        if (_mouse is null)
            return;

        Vector2 position = _mouse.Position;
        if (!_mouse.IsButtonPressed(MouseButton.Left))
        {
            _wasDragging = false;
            _lastMousePosition = position;
            return;
        }

        if (!_wasDragging)
        {
            _wasDragging = true;
            _lastMousePosition = position;
            return;
        }

        Vector2 delta = position - _lastMousePosition;
        _lastMousePosition = position;
        Camera = Camera with
        {
            YawRadians = Camera.YawRadians + delta.X * 0.004f,
            PitchRadians = Math.Clamp(
                Camera.PitchRadians - delta.Y * 0.004f,
                -1.55f,
                1.55f)
        };
    }

    private void ApplyPicking(IWindow window)
    {
        bool isPickKeyPressed = _keyboard?.IsKeyPressed(Key.P) == true;
        if (isPickKeyPressed && !_wasPickKeyPressed)
        {
            PickUnderMouse(window);
            CopyCurrentPickToClipboard();
        }

        _wasPickKeyPressed = isPickKeyPressed;
    }

    private void PickUnderMouse(IWindow window)
    {
        if (_mouse is null)
            return;

        Vector2D<int> size = window.Size;
        Vector2 viewport = new(Math.Max(1, size.X), Math.Max(1, size.Y));
        _selectedPickCandidates = MapRenderPicker.PickCandidates(
            _scene,
            Camera,
            _mouse.Position,
            viewport,
            includeUntexturedGeometry: false,
            includeCollision: false);
        if (MapRenderPicker.TryPick(
                _scene,
                Camera,
                _mouse.Position,
                viewport,
                includeUntexturedGeometry: false,
                includeCollision: false,
                out MapRenderPickHit hit))
        {
            _selectedPick = hit;
            _selectedNeighborCandidates = MapRenderPicker.FindNearbyCandidates(
                _scene,
                hit.Position,
                includeUntexturedGeometry: false,
                includeCollision: false);
            _lastPickMiss = false;
            return;
        }

        _selectedPick = null;
        _selectedNeighborCandidates = [];
        _lastPickMiss = true;
    }

    private void CopyCurrentPickToClipboard()
    {
        if (_copyTextAsync is null)
            return;

        string text = MapRenderPickClipboardFormatter.Format(
            _scene,
            Camera,
            _selectedPick,
            _selectedPickCandidates,
            _selectedNeighborCandidates,
            _lastPickMiss);
        _ = _copyTextAsync(text);
    }

    public void Dispose()
    {
        _input?.Dispose();
        _input = null;
        _keyboard = null;
        _mouse = null;
    }
}

internal static class NativeMapRenderCamera
{
    internal static RenderCamera CreateInitial(
        RenderBounds bounds,
        string? mapEntityString)
    {
        return TryCreateGlobalIntermission(
                bounds,
                mapEntityString,
                out RenderCamera camera)
            ? camera
            : CreateBoundsCamera(bounds);
    }

    private static RenderCamera CreateBoundsCamera(RenderBounds bounds)
    {
        const float previewNearPlane = 4f;
        float radius = bounds.Radius;
        float targetHeight = bounds.IsValid
            ? bounds.Center.Y - radius * 0.001f
            : 0f;
        Vector3 target = new(
            radius * 0.04f,
            targetHeight,
            -radius * 0.074f);
        Vector3 position = target + new Vector3(
            -radius * 0.14f,
            radius * 0.02f,
            radius * 0.14f);
        Vector3 direction = Vector3.Normalize(target - position);
        return new RenderCamera(
            position,
            MathF.Atan2(direction.X, -direction.Z),
            MathF.Asin(direction.Y),
            55f * MathF.PI / 180f,
            previewNearPlane,
            MathF.Max(250000f, position.Y + radius * 4f));
    }

    private static bool TryCreateGlobalIntermission(
        RenderBounds bounds,
        string? mapEntityString,
        out RenderCamera camera)
    {
        camera = default;
        if (string.IsNullOrWhiteSpace(mapEntityString))
            return false;

        ReadOnlySpan<char> source = mapEntityString.AsSpan();
        int sourceOffset = 0;
        while (TryReadEntity(source, ref sourceOffset, out ReadOnlySpan<char> entity))
        {
            if (!TryReadGlobalIntermission(
                    entity,
                    out Vector3 gameOrigin,
                    out Vector3 gameAngles))
            {
                continue;
            }

            const float previewNearPlane = 4f;
            const float degreesToRadians = MathF.PI / 180f;
            Vector3 position =
                RenderCoordinateConverter.GameToRenderPosition(gameOrigin);
            float yaw = NormalizeDegrees(90f - gameAngles.Y) *
                degreesToRadians;
            float pitch = -NormalizeDegrees(gameAngles.X) *
                degreesToRadians;
            camera = new RenderCamera(
                position,
                yaw,
                pitch,
                55f * degreesToRadians,
                previewNearPlane,
                MathF.Max(250000f, position.Y + bounds.Radius * 4f));
            return true;
        }

        return false;
    }

    private static bool TryReadEntity(
        ReadOnlySpan<char> source,
        ref int sourceOffset,
        out ReadOnlySpan<char> entity)
    {
        entity = default;
        int relativeStart = source[sourceOffset..].IndexOf('{');
        if (relativeStart < 0)
            return false;

        int entityStart = sourceOffset + relativeStart + 1;
        bool quoted = false;
        bool escaped = false;
        for (int index = entityStart; index < source.Length; index++)
        {
            char current = source[index];
            if (quoted)
            {
                if (escaped)
                    escaped = false;
                else if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    quoted = false;
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == '}')
            {
                entity = source[entityStart..index];
                sourceOffset = index + 1;
                return true;
            }
        }

        sourceOffset = source.Length;
        return false;
    }

    private static bool TryReadGlobalIntermission(
        ReadOnlySpan<char> entity,
        out Vector3 origin,
        out Vector3 angles)
    {
        origin = default;
        angles = default;
        bool isGlobalIntermission = false;
        bool hasOrigin = false;
        bool hasAngles = false;
        int offset = 0;
        while (offset < entity.Length)
        {
            SkipWhitespace(entity, ref offset);
            if (offset == entity.Length)
                break;
            if (!TryReadQuotedToken(entity, ref offset, out ReadOnlySpan<char> key))
                return false;

            SkipWhitespace(entity, ref offset);
            if (!TryReadQuotedToken(entity, ref offset, out ReadOnlySpan<char> value))
                return false;

            if (key.Equals("classname", StringComparison.OrdinalIgnoreCase))
            {
                isGlobalIntermission = value.Equals(
                    "mp_global_intermission",
                    StringComparison.OrdinalIgnoreCase);
            }
            else if (key.Equals("origin", StringComparison.OrdinalIgnoreCase))
                hasOrigin = TryParseVector3(value, out origin);
            else if (key.Equals("angles", StringComparison.OrdinalIgnoreCase))
                hasAngles = TryParseVector3(value, out angles);
        }

        return isGlobalIntermission && hasOrigin && hasAngles;
    }

    private static bool TryReadQuotedToken(
        ReadOnlySpan<char> source,
        ref int offset,
        out ReadOnlySpan<char> value)
    {
        value = default;
        if (offset >= source.Length || source[offset] != '"')
            return false;

        int valueStart = ++offset;
        bool escaped = false;
        while (offset < source.Length)
        {
            char current = source[offset];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current == '"')
            {
                value = source[valueStart..offset];
                offset++;
                return true;
            }
            offset++;
        }

        return false;
    }

    private static bool TryParseVector3(
        ReadOnlySpan<char> source,
        out Vector3 value)
    {
        value = default;
        Span<float> components = stackalloc float[3];
        int offset = 0;
        for (int component = 0; component < components.Length; component++)
        {
            SkipWhitespace(source, ref offset);
            int start = offset;
            while (offset < source.Length && !char.IsWhiteSpace(source[offset]))
                offset++;
            if (start == offset ||
                !float.TryParse(
                    source[start..offset],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out components[component]) ||
                !float.IsFinite(components[component]))
            {
                return false;
            }
        }

        SkipWhitespace(source, ref offset);
        if (offset != source.Length)
            return false;

        value = new Vector3(components[0], components[1], components[2]);
        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> source, ref int offset)
    {
        while (offset < source.Length && char.IsWhiteSpace(source[offset]))
            offset++;
    }

    private static float NormalizeDegrees(float degrees)
    {
        float normalized = degrees % 360f;
        if (normalized > 180f)
            return normalized - 360f;
        if (normalized <= -180f)
            return normalized + 360f;
        return normalized;
    }
}
