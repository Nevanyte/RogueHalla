#ifndef RH_STORM_INK_HALL_INCLUDED
#define RH_STORM_INK_HALL_INCLUDED

TEXTURE2D(_StormTempleStone);
float4 _StormHallPlan;   // active, painted stone present, distance from entrance to shrine door, unused
float4 _StormHallAxis;   // entrance.xy, shrine.xy in map tiles

void StormHallSurface(float2 t, float dEdge, out float3 colour, out float3 emission, out float inside)
{
    float2 delta = _StormHallAxis.zw - _StormHallAxis.xy;
    float span = max(length(delta), 0.01);
    float2 axis = delta / span;
    float2 offset = t - _StormHallAxis.xy;
    float forward = dot(offset, axis);
    float crossAisle = dot(offset, float2(-axis.y, axis.x));
    float side = abs(crossAisle);

    // Stone is clipped to the delivered footprint. All central inlay is evaluated in entrance-to-shrine coordinates.
    inside = smoothstep(-0.08, 0.18, dEdge);
    float2 stoneUv = float2(crossAisle, -forward) / 10.0;
    float3 stone = _TempleStone.rgb * (0.89 + VNoise(t * 0.52) * 0.16);
    [branch] if (_StormHallPlan.y > 0.5)
        stone = lerp(stone, SAMPLE_TEXTURE2D(_StormTempleStone, sampler_GroundTex, stoneUv).rgb, 0.78);

    // Sun-bleached sandstone has broad cool and warm washes. Break the court edge
    // against the sand with a narrow band of worn pigment.
    float grain = saturate(dot(stone, float3(.22, .65, .13)));
    float wash = VNoise(t * .33);
    inside *= smoothstep(-.24, .32, dEdge + (wash - .5) * .62);
    stone = lerp(float3(.245, .228, .175), float3(.65, .572, .405), saturate(grain * 1.17 + (wash - .5) * .19));
    float2 courtCell = float2(crossAisle / 3.2 + .5 * fmod(abs(floor(forward / 1.7)), 2.0), forward / 1.7);
    float2 courtJoints = abs(frac(courtCell + .5) - .5);
    stone *= .92 + .08 * smoothstep(.0, .026, min(courtJoints.x, courtJoints.y));
    float border = (1.0 - smoothstep(.04, .15, abs(dEdge - .58))) * .31;
    stone = lerp(stone, float3(.28, .215, .13), border);

    float doorApproach = smoothstep(_StormHallPlan.z - 2.2, _StormHallPlan.z - .25, forward);
    float aisleHalf = lerp(2.08, 1.12, doorApproach);
    float runner = smoothstep(-.56, -.30, forward) * (1.0 - smoothstep(span - .12, span + .52, forward));
    float inset = (1.0 - smoothstep(aisleHalf - .05, aisleHalf + .04, side)) * runner;

    // Three staggered runs of mineral inlay. Thin stone joints and abrasion expose
    // the same underlying paving as the bays, especially at the threshold.
    float row = floor((forward + .5) / 1.67);
    float2 slab = float2(crossAisle / 1.35 + .50 + .5 * fmod(abs(row), 2.0), (forward + .5) / 1.67);
    float2 joints = min(frac(slab), 1.0 - frac(slab)) * float2(1.35, 1.67);
    float edge = min(joints.x, joints.y);
    float fitted = smoothstep(.019, .062, edge);
    float pigment = saturate(.37 + .42 * (VNoise(floor(slab) + 14.7) - .5) + .86 * (grain - .53) + .38 * (wash - .5));
    float3 mineral = lerp(float3(.052, .137, .124), float3(.152, .331, .269), pigment);
    float wear = smoothstep(.58, .80, grain + (wash - .5) * .21);
    mineral = lerp(mineral, stone, .10 + .42 * wear);
    colour = lerp(stone, mineral, inset * (.47 + .53 * fitted));
    float brass = (1.0 - smoothstep(.023, .073, abs(side - aisleHalf + .13))) * runner;
    colour = lerp(colour, float3(.44, .307, .14), brass * (.64 - wear * .30));
    emission = 0;
}

#endif
