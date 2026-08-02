# IW4 renderer architecture

`IW4.Render` owns backend-neutral scene construction, immutable frame inputs,
RSX shader/material interpretation, visibility, picking, and draw planning.
It has no windowing or OpenGL context dependency.

`IW4.Render.OpenGl` owns Silk.NET/OpenGL resources, program compilation,
command execution, presentation, and live FPS/GPU telemetry.

`IW4.Studio` turns the loaded runtime asset graph into a render scene.
`IW4.Studio.Desktop` owns the map window, input, clipboard picker output, and
renderer lifetime.

The immutable scene and draw-packet types are execution data shared between
scene construction and the OpenGL thread. They are product state, not
validation records.
