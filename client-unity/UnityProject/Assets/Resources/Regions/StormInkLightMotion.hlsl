#ifndef RH_STORM_INK_LIGHT_MOTION_INCLUDED
#define RH_STORM_INK_LIGHT_MOTION_INCLUDED

// Tile-anchored brush marks use the same atmosphere clock as the sun and local lamps.

// Station increases downstream; cross is measured to the side of the connected channel.
// Break the crests into a few strokes. Reeds, the bed and the waterline stay in place.
float InkWaterReflection(float2 t, float wet, float4 channel)
{
    if (_StormFlowOn < .5) channel = float4(0, 0, wet, 0);
    float depth = smoothstep(.08, .74, channel.z);
    float s = channel.x - _RhInkClock.x * .29;
    float2 water = float2(s, channel.y);
    float crest = s * 1.08 - .21 * abs(channel.y) + .21 * VNoise(water * float2(.71, 1.88));
    float ribbon = 1.0 - smoothstep(.015, .10, abs(frac(crest) - .5));
    float breaks = smoothstep(.32, .78, VNoise(water * float2(.74, 3.12) + 7.1));
    // A quiet pool catches short ripples parallel to the rim. Its shore has no downstream conveyor.
    float bow = channel.z * 2.35 + VNoise(t * 1.18) * .44 - _RhInkClock.x * .083;
    float pool = (1.0 - smoothstep(.018, .095, abs(frac(bow) - .5))) * smoothstep(.39, .71, VNoise(t * 1.07));
    return wet * depth * lerp(pool * .48, ribbon * breaks, channel.w) * _RhInkClock.z;
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

// The painterly borders are joined once when the repeating ground array is built.
float4 ElementalPaint(float2 uv, int slice)
{
    return SAMPLE_TEXTURE2D_ARRAY(_GroundTex, sampler_GroundTex, uv, slice);
}

// A chine of broken indigo and a small pale edge give a fracture its thickness.
// Sweep patches, gaps in the veins and the mineral grain stay attached to the ice.
void InkIceSurface(float2 t, float inner, float3 glow, out float3 colour, out float3 emission, out float height)
{
    float2 uv = float2(t.x + t.y * .12, t.x * .10 - t.y) / 7.8;
    uv += (VNoise(t * .23 + 32) - .5) * .045;
    float4 ink = ElementalPaint(uv, 6);
    if (ink.a > .5)
    {
        float blue = saturate(ink.b - ink.r);
        float frost = smoothstep(.29, .62, ink.r);
        float rim = smoothstep(.50, .79, ink.r) * smoothstep(.57, .82, ink.g);
        float3 mineral = SRGBToLinear(ink.rgb);
        float depth = .56 + .30 * frost;
        if (_RhSwitch.z > .5)
            depth += .19 * VNoise(t * .44 + (t - _RhCam.xy) * .065);
        colour = mineral * depth + float3(.003, .010, .032);
        colour = lerp(colour, float3(.69,.80,.83), .52 * rim);
        colour = lerp(colour, float3(.41,.69,.76), (1.0 - inner) * .15);
        // Cold light travels under translucent pigment; only a fractured lip catches white.
        float glint = InkIceGlint(t, rim, blue * .22);
        emission = blue * float3(.003,.025,.089) * inner + rim * float3(.10,.10,.083)
            + float3(.33,.71,.94) * glint * .72;
        height = rim * .018 - blue * .009;
        return;
    }
    float2 brush = float2(dot(t, float2(.54, .23)), dot(t, float2(-.36, 1.31)));
    float paper = VNoise(t * .72 + 7.2);
    float wash = Fbm(brush * float2(.48, 1.7));
    float score = Worley(brush * float2(.86, .71) + paper * .34).y;
    float vein = (1.0 - smoothstep(.016, .084, score)) * smoothstep(.27, .67, paper);
    float edge = (1.0 - smoothstep(.012, .050, abs(score - .099))) * smoothstep(.42, .73, wash);
    float shore = 1.0 - smoothstep(.08, .78, inner);
    float rake = VNoise(brush * float2(.92, 8.5));
    float streak = smoothstep(.55, .82, rake) * smoothstep(.43, .67, wash);
    colour = lerp(float3(.050, .078, .10), float3(.30, .37, .39), smoothstep(.20, .80, wash));
    colour = lerp(colour, float3(.48, .54, .53), shore * .51 + streak * .16);
    colour *= 1.0 - vein * .47;
    colour += float3(.13, .15, .15) * edge;
    float glint = InkIceGlint(t, max(edge, vein * .34), streak);
    emission = glow * glint * .53;
    if (_RhSwitch.z > .5)
    {
        float depth = VNoise(brush * .74 + (t - _RhCam.xy) * .065);
        colour *= lerp(1.0, .72 + depth * .31, inner);
    }
    height = .020 * edge - .030 * vein;
}

float InkLavaPulse(float2 t)
{
    // A broad moving seam of heat below the dry basalt crust, visible without bloom.
    float pigment = VNoise(t * .47 + 14.1);
    return .89 + .11 * sin(dot(t, float2(.49, -.25)) + pigment * 3.0 - _RhInkClock.x * 1.06);
}

// Paint the length of the channel along the original painting's diagonal stroke.
// The wet interior carries pigment past a slower fold; the rough cooling shelf is fixed to rock.
float2 LavaPaintUV(float station, float across)
{
    return float2(.070 * station + .23 * across, .070 * station - .23 * across);
}

void InkLavaSurface(float2 t, float inner, float4 channel, float3 glow, out float3 colour, out float3 emission, out float height)
{
    // On devices without the field format, retain a still molten painting and its shore.
    if (_StormFlowOn < .5) channel = float4(t.x, -t.y, inner, 0);
    float bankGrain = VNoise(t * 3.15 + 9.4);
    float shore = smoothstep(.03, .54, inner);
    float depth = smoothstep(.09, .75, channel.z);
    float flow = channel.w;
    float station = channel.x;
    float across = channel.y;
    // A small stationary warp keeps hand-painted edges irregular all the way round a bend.
    across += (VNoise(t * .86) - .5) * .38;
    float time = _StormFlowOn > .5 ? _RhInkClock.x : 0.0;
    float2 fastUV = LavaPaintUV(station - .56 * time, across);
    float2 slowUV = LavaPaintUV(station - .16 * time + 2.4, across + .48);
    // Broad basins expose more stationary crust. Cross-fade sampled washes, never unrelated UV origins.
    float2 basinUV = float2(t.x, -t.y) / 5.7;
    float4 fast = ElementalPaint(fastUV, 7);
    float4 slow = ElementalPaint(slowUV, 7);
    [branch] if (flow < .48)
    {
        float2 swirl = .013 * depth * float2(sin(time * .21 + channel.z * 2.2), cos(time * .16 + across));
        float4 basin = ElementalPaint(basinUV + swirl, 7);
        slow = lerp(basin, slow, smoothstep(.06, .48, flow));
    }
    float4 ink = lerp(slow, fast, smoothstep(.08, .84, depth) * (.23 + .75 * flow));
    float liquid = smoothstep(.33, .87, ink.r);
    float gold = smoothstep(.19, .73, ink.g) * liquid;
    float white = smoothstep(.38, .86, ink.b) * gold;
    // Keep the outer stroke thick where the lip overhangs, thin where a hot seam meets rock.
    float edge = min(inner + bankGrain * .10, channel.z);
    float exposed = smoothstep(.018 + bankGrain * .08, .22 + bankGrain * .13, edge);
    float3 shelf = lerp(float3(.045,.014,.021), float3(.16,.049,.030), bankGrain);
    if (ink.a <= .5)
    {
        float f = VNoise(float2(station * .68 - time * .22, across * 3.4));
        liquid = smoothstep(.22,.63,f);
        gold = smoothstep(.51,.73,f);
        white = gold * smoothstep(.70,.84,f);
        ink.rgb = lerp(float3(.26,.065,.037), float3(1.0,.67,.13), liquid);
    }
    float3 pigment = SRGBToLinear(ink.rgb) * (.59 + .15 * depth) + float3(.018,.005,.007);
    colour = lerp(shelf, pigment, exposed);
    float pulse = .94 + .06 * sin(station * .72 - time * .68 + across * .26);
    emission = (float3(.49,.082,.012) * liquid + float3(.74,.55,.10) * gold
        + float3(.49,.49,.26) * white) * exposed * pulse;
    // Only small breaks in the cooled lip catch a red line from the fluid underneath.
    float crack = (1.0 - exposed) * smoothstep(.47,.79,bankGrain) * shore;
    emission += glow * (.14 * crack);
    height = (1.0 - exposed) * .039 - exposed * (.030 + .022 * liquid);
}

// Ember terrain is cooling cinder. Short, disconnected flecks sit inside the ash.
void InkEmberSurface(float2 t, float3 glow, out float3 colour, out float3 emission, out float height)
{
    float2 brush = float2(dot(t, float2(.84, .33)), dot(t, float2(-.41, 1.1)));
    float grain = Fbm(brush * 2.1);
    float scratch = VNoise(brush * float2(2.3, 8.5) + grain);
    float fleck = smoothstep(.72, .88, scratch) * smoothstep(.49, .69, grain);
    float pulse = .80 + .20 * sin(_RhInkClock.x * 1.4 + floor(brush.x * 2.0));
    colour = lerp(float3(.045, .032, .039), float3(.158, .093, .078), smoothstep(.20, .79, grain));
    colour += fleck * float3(.40, .18, .045);
    emission = glow * (.014 + fleck * .55 * pulse);
    height = (grain - .5) * .035;
}
#endif
