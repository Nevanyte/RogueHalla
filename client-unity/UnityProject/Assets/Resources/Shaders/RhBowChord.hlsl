#ifndef RH_BOW_CHORD_INCLUDED
#define RH_BOW_CHORD_INCLUDED
// Coordinates already include the canonical atlas cell offset. Keep the painted
// nocks, even when the two drawn halves open well outside the original chord.
float RhStraightBowStringCoverage(float2 uv, float4 chord, float width)
{
    if (width <= 0.0) return 1.0;
    float along = (uv.y - chord.z) / max(chord.w - chord.z, 0.0001);
    if (along <= 0.018 || along >= 0.982) return 1.0;
    float nearNock = min(along, 1.0 - along);
    float cut = width * lerp(0.57, 1.0, smoothstep(0.018, 0.12, nearNock));
    return smoothstep(cut * 0.85, cut, abs(uv.x - lerp(chord.x, chord.y, along)));
}
#endif
