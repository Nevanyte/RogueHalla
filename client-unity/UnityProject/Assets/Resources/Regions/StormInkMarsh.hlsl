#ifndef RH_STORM_INK_MARSH_INCLUDED
#define RH_STORM_INK_MARSH_INCLUDED

// The ground's repeat sampler also serves the painted marsh wash.
TEXTURE2D(_StormMarshWash);
float _StormMarshOn;

float3 StormMarshPaint(float2 t)
{
    return SAMPLE_TEXTURE2D(_StormMarshWash, sampler_GroundTex, float2(t.x, -t.y) / 17.0).rgb;
}

// Class weights come from the existing terrain map. The wash supplies the paper and brush marks at every bank,
// including the unwalkable soil directly under the trunk, which has no walkable-surface classification.
float3 StormMarshFloor(float2 t, float4 plg)
{
    float3 wash = StormMarshPaint(t);
    float ink = saturate((dot(wash, float3(.24, .62, .14)) - .23) * 1.86);
    float grass = smoothstep(.18, .81, plg.g);
    float water = smoothstep(.20, .79, plg.r);
    float3 mud = lerp(float3(.094, .065, .050), float3(.265, .200, .152), ink);
    float3 sedge = lerp(float3(.081, .105, .079), float3(.241, .276, .190), ink);
    float3 silt = lerp(mud, sedge, grass);
    float wetBank = 4.0 * water * (1.0 - water);
    silt *= 1.0 - .22 * wetBank;
    // A walkable shallow shows its earth through broken reflections. Only the bog hazard fills a channel
    // with an unbroken wash, so the narrow dry crossing survives a decoration band passing over it.
    float3 sky = lerp(float3(.095, .143, .137), float3(.251, .291, .256), ink);
    float reflection = lerp(.25, .62, smoothstep(.26, .78, ink));
    return lerp(silt, sky, water * reflection);
}

void StormMarshChannel(float2 t, float inner, inout float3 colour, inout float3 emission)
{
    float3 wash = StormMarshPaint(t);
    float pigment = saturate((dot(wash, float3(.24, .62, .14)) - .23) * 1.86);
    float3 stillWater = lerp(float3(.075, .122, .123), float3(.235, .310, .283), pigment);
    float brokenRim = (1.0 - smoothstep(.10, .38, inner)) * smoothstep(.29, .72, pigment);
    colour = lerp(stillWater, float3(.33, .356, .276), brokenRim * .62);
    emission *= .018;
}

#endif
