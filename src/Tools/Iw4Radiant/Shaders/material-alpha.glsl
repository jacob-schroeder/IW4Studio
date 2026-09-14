uniform int uAlphaTest;

void applyMaterialAlpha(float alpha)
{
    if ((uAlphaTest == 1 && alpha <= 0.0) ||
        (uAlphaTest == 2 && alpha >= 128.0 / 255.0) ||
        (uAlphaTest == 3 && alpha < 128.0 / 255.0))
        discard;
}
