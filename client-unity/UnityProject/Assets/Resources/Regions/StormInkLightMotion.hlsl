#ifndef RH_STORM_INK_LIGHT_MOTION_INCLUDED
#define RH_STORM_INK_LIGHT_MOTION_INCLUDED

// Tile-anchored brush marks share the atmosphere clock. x freezes with Reduced Motion,
// y enables ornamental movement, z enables the quality-controlled glints.
float4 _RhInkClock;

float InkWaterReflection(float2 t, float wet)
{
    float grain = VNoise(float2(t.x * .84 + t.y * .18, t.y * .52));
    float sweep = t.y * 2.46 + t.x * .39 + grain * .62 - _RhInkClock.x * .095;
    float band = 1.0 - smoothstep(.015, .095, abs(frac(sweep) - .5));
    return wet * band * smoothstep(.43, .76, grain) * _RhInkClock.z;
}

float InkBogMeniscus(float2 t)
{
    float2 cell = floor(t * .66);
    float seed = H21(cell);
    float phase = frac(_RhInkClock.x * .16 + seed);
    float2 p = frac(t * .66) - (.30 + H22(cell + 9.0) * .40);
    float radius = length(p * float2(.81, 1.28));
    float lip = 1.0 - smoothstep(.009, .029, abs(radius - .045 - .135 * phase));
    return lip * smoothstep(.02, .24, phase) * (1.0 - smoothstep(.47, .76, phase))
        * smoothstep(-.04, .08, p.y - .27 * p.x) * step(.70, seed) * _RhInkClock.y;
}

float InkIceGlint(float2 t, float fissure, float streak)
{
    float grain = VNoise(t * .76 + 33.7);
    float phase = frac(dot(t, float2(.31, -.14)) + grain * .51 - _RhInkClock.x * .074);
    float mark = 1.0 - smoothstep(.009, .050, abs(phase - .5));
    return mark * max(fissure, streak * .46) * smoothstep(.36, .77, grain) * _RhInkClock.z;
}

float InkLavaPulse(float2 t)
{
    // A broad moving seam of heat below the dry basalt crust, visible without bloom.
    float pigment = VNoise(t * .47 + 14.1);
    return .89 + .11 * sin(dot(t, float2(.49, -.25)) + pigment * 3.0 - _RhInkClock.x * 1.06);
}
#endif
