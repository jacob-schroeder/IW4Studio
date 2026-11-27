uniform sampler2D uSource;
uniform int uPass;
uniform float uTime;
uniform int uAxis;
uniform int uSpan;
uniform int uSize;
out vec4 result;

int reverseIndex(int index)
{
    int reversed = 0;
    for (int remaining = uSize; remaining > 1; remaining >>= 1)
    {
        reversed = (reversed << 1) | (index & 1);
        index >>= 1;
    }
    return reversed;
}

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    if (uPass == 0)
    {
        // Correlated WaterFrequenciesAtTime: quantized 1024-entry sine phases,
        // including the original +255 real-component phase offset.
        vec3 spectrum = texelFetch(uSource, pixel, 0).xyz;
        int phase = int(mod(trunc(spectrum.z * (uTime * 162.9746551513672)), 1024.0));
        vec2 angle = vec2((phase + 255) & 1023, phase) * (6.283185307179586 / 1024.0);
        result = vec4(spectrum.z == 0.0 ? vec2(0.0) : spectrum.xy * sin(angle), 0.0, 1.0);
    }
    else if (uPass == 1)
    {
        int coordinate = uAxis == 0 ? pixel.x : pixel.y;
        int halfSpan = uSpan / 2;
        int offset = coordinate % uSpan;
        int evenIndex = coordinate - offset + offset % halfSpan;
        int oddIndex = evenIndex + halfSpan;
        if (uSpan == 2)
        {
            evenIndex = reverseIndex(evenIndex);
            oddIndex = reverseIndex(oddIndex);
        }
        ivec2 evenPixel = uAxis == 0 ? ivec2(evenIndex, pixel.y) : ivec2(pixel.x, evenIndex);
        ivec2 oddPixel = uAxis == 0 ? ivec2(oddIndex, pixel.y) : ivec2(pixel.x, oddIndex);
        vec2 a = texelFetch(uSource, evenPixel, 0).xy;
        vec2 b = texelFetch(uSource, oddPixel, 0).xy;
        float angle = 6.283185307179586 * float(offset % halfSpan) / float(uSpan);
        vec2 twiddle = vec2(cos(angle), sin(angle));
        vec2 rotated = vec2(b.x * twiddle.x - b.y * twiddle.y, b.x * twiddle.y + b.y * twiddle.x);
        result = vec4(offset < halfSpan ? a + rotated : a - rotated, 0.0, 1.0);
    }
    else
    {
        // WaterPixelsFromAmplitudes stores the complex magnitude as 8-bit luminance.
        float height = min(length(texelFetch(uSource, pixel, 0).xy) / float(uSize * uSize), 1.0);
        height = floor(height * 255.9989929199219) / 255.0;
        result = vec4(height, height, height, 1.0);
    }
}
