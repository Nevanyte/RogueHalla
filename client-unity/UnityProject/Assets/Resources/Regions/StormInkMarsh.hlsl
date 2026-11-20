#ifndef RH_STORM_INK_MARSH_INCLUDED
#define RH_STORM_INK_MARSH_INCLUDED

// The ground's repeat sampler also serves the painted marsh wash.
TEXTURE2D(_StormMarshWash);
float _StormMarshOn;

float3 StormMarshPaint(float2 t)
{
    // Each pigment strip overlaps its opposite edge in the texture. The world-space bend
    // varies the direction of the strokes without reflecting grass tufts into chevrons.
    float2 uv = float2(t.x * .96 + t.y * .28, t.x * .28 - t.y * .96) / 8.5;
    uv += float2(.16 * sin(t.y * .14 + t.x * .05), .19 * sin(t.x * .13 - t.y * .07));
    return SAMPLE_TEXTURE2D(_StormMarshWash, sampler_GroundTex, uv).rgb;
}

// Class weights come from the existing terrain map. The wash supplies the paper and brush marks at every bank,
// including the unwalkable soil directly under the trunk, which has no walkable-surface classification.
float3 StormMarshFloor(float2 t, float4 plg)
{
    float3 wash = StormMarshPaint(t);
    // The three linear masks are actual strokes sampled from the approved peat, sedge and shallow
    // water. A dry crossing keeps its peat grain even beside a bright, moving marsh channel.
    float grass = smoothstep(.18, .81, plg.g);
    float water = smoothstep(.20, .79, plg.r);
    float3 mud = lerp(float3(.110, .078, .058), float3(.250, .180, .135), wash.r);
    float3 sedge = lerp(float3(.089, .107, .070), float3(.201, .226, .149), wash.g);
    float3 silt = lerp(mud, sedge, grass);
    silt *= 1.0 - .18 * 4.0 * water * (1.0 - water);
    float3 sky = lerp(float3(.106, .153, .136), float3(.257, .290, .234), wash.b);
    float reflection = lerp(.32, .62, wash.b);
    return lerp(silt, sky, water * reflection);
}

void StormMarshChannel(float2 t, float inner, inout float3 colour, inout float3 emission)
{
    float3 wash = StormMarshPaint(t);
    float pigment = .17 + .66 * wash.b;
    float3 stillWater = lerp(float3(.075, .122, .123), float3(.235, .310, .283), pigment);
    float brokenRim = (1.0 - smoothstep(.10, .38, inner)) * smoothstep(.29, .72, pigment);
    colour = lerp(stillWater, float3(.33, .356, .276), brokenRim * .62);
    emission *= .018;
}

#endif
