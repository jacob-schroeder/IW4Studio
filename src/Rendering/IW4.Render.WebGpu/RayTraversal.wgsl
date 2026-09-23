struct Node {
    minimum: vec3<f32>,
    escape: u32,
    maximum: vec3<f32>,
    surface: i32,
}

struct Ray {
    origin: vec3<f32>,
    padding0: f32,
    direction: vec3<f32>,
    padding1: f32,
}

struct Parameters {
    ray_count: u32,
    stride: u32,
    padding0: u32,
    padding1: u32,
}

@group(0) @binding(0) var<storage, read> nodes: array<Node>;
@group(0) @binding(1) var<storage, read> rays: array<Ray>;
@group(0) @binding(2) var<storage, read_write> candidates: array<i32>;
@group(0) @binding(3) var<uniform> parameters: Parameters;

fn intersects(node: Node, ray: Ray) -> bool {
    var enter = 0.0f;
    // Host validation bounds every possible endpoint below 2^62.
    var exit = 1e30f;
    for (var axis = 0u; axis < 3u; axis++) {
        let origin = ray.origin[axis];
        let direction = ray.direction[axis];
        // This is a conservative candidate query. The CPU rechecks every
        // returned leaf using its original double-precision bounds predicate.
        // For host-validated coordinates <= 2^30 and nonzero directions >=
        // 2^-30, this slack covers WGSL division error and subnormal flushing.
        let magnitude = max(abs(node.minimum[axis]), abs(node.maximum[axis]));
        let padding = (abs(origin) + magnitude) * 0.00006103515625f + 0.001f;
        let lower = node.minimum[axis] - padding;
        let upper = node.maximum[axis] + padding;
        if (direction == 0.0f) {
            if (origin < lower || origin > upper) { return false; }
            continue;
        }
        let first = (lower - origin) / direction;
        let second = (upper - origin) / direction;
        enter = max(enter, min(first, second));
        exit = min(exit, max(first, second));
        if (enter > exit) { return false; }
    }
    return true;
}

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    if (id.x >= parameters.ray_count) { return; }
    let first = id.x * parameters.stride;
    let ray = rays[id.x];
    var cursor = 0u;
    var count = 0u;
    while (cursor < arrayLength(&nodes)) {
        let node = nodes[cursor];
        if (!intersects(node, ray)) {
            cursor = node.escape;
            continue;
        }
        cursor++;
        if (node.surface < 0) { continue; }
        if (count + 1u == parameters.stride) {
            // An overflowing ray is evaluated by the original CPU query.
            candidates[first] = -1;
            return;
        }
        candidates[first + 1u + count] = node.surface;
        count++;
    }
    candidates[first] = i32(count);
}
