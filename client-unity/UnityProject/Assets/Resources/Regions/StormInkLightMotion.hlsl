#ifndef RH_STORM_INK_LIGHT_MOTION_INCLUDED
#define RH_STORM_INK_LIGHT_MOTION_INCLUDED

// Tile-anchored brush marks use the same atmosphere clock as the sun and local lamps.

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

// A broad red current carries long gold strokes past dark rafts of crust. The bank
// and the flowing interior are both bounded by the delivered molten terrain.
void InkLavaSurface(float2 t, float inner, float4 sides, float3 glow, out float3 colour, out float3 emission, out float height)
{
    float2 bank = float2(sides.x - sides.y, sides.z - sides.w);
    float2 axis = float2(bank.y, -bank.x);
    if (dot(axis, float2(.82,-.57)) < 0) axis = -axis;
    float2 current = normalize(axis + float2(.60,-.43));
    float2 uv = float2(t.x, -t.y) / 6.4 + (.034 * VNoise(t * .43));
    float2 travel = float2(current.x, -current.y) * .16;
    float phase = frac(_RhInkClock.x * .145);
    float4 first = ElementalPaint(uv - travel * (phase - .5), 7);
    float4 next = ElementalPaint(uv - travel * (frac(phase + .5) - .5), 7);
    float4 ink = lerp(first, next, abs(phase * 2.0 - 1.0));
    if (ink.a > .5)
    {
        float liquid = smoothstep(.34, .87, ink.r);
        float gold = smoothstep(.22, .74, ink.g) * liquid;
        float white = smoothstep(.39, .87, ink.b) * gold;
        float3 pigment = SRGBToLinear(ink.rgb);
        colour = pigment * (.58 + inner * .16) + float3(.016,.005,.006);
        emission = (float3(.48,.070,.010) * liquid + float3(.72,.56,.10) * gold
            + float3(.52,.53,.29) * white) * InkLavaPulse(t);
        height = (1.0 - liquid) * .046;
        return;
    }
    float2 flow = float2(.69*t.x + .30*t.y, -.23*t.x + .91*t.y);
    float bend = VNoise(t * .43) * 1.35 + VNoise(t * 1.11) * .28;
    float2 p = float2(flow.x * .50, flow.y * 2.1 + bend * 2.9);
    float grain = Fbm(p + float2(-_RhInkClock.x * .105, 0));
    float crust = smoothstep(.57, .68, grain + .089 * (1.0 - inner));
    float brush = 1.0 - smoothstep(.018, .14, abs(frac(flow.y * .94 + bend - grain * .47) - .50));
    float gold = brush * smoothstep(.27,.61,grain) * (1.0 - crust);
    float strand = VNoise(p * float2(2.1, 3.7) + float2(-_RhInkClock.x * .21, 0));
    float tip = smoothstep(.51, .82, strand) * gold;
    float core = (1.0 - crust) * (.10 + .40 * inner) * (.67 + .60 * grain);
    colour = lerp(float3(.22, .037, .015), float3(.55, .155, .027), smoothstep(.23, .68, grain));
    colour = lerp(colour, float3(.063, .036, .031) * (.72 + .62 * grain), crust);
    emission = (float3(.96, .13, .018) * core + float3(.8, .42, .08) * gold
        + float3(.60, .48, .14) * tip + glow * (.030 * inner)) * InkLavaPulse(t);
    height = .046 * crust - .012 * gold;
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
