#ifndef RH_STORM_INK_REGION_EDGES_INCLUDED
#define RH_STORM_INK_REGION_EDGES_INCLUDED

// Linear interpolation on split triangles gives short angular wind cuts with no repeated zigzag.
float EdgeDryBrush(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float b = H21(i + float2(1, 0)), c = H21(i + float2(0, 1));
    if (f.x + f.y < 1.0) return dot(float3(1.0 - f.x - f.y, f.x, f.y), float3(H21(i), b, c));
    return dot(float3(f.x + f.y - 1.0, 1.0 - f.y, 1.0 - f.x), float3(H21(i + 1.0), b, c));
}

// Erode each individual stone in its own authored course. Big slabs lose an oblique
// corner and split along the grain. Only small detached pieces survive at the outer lip.
float EdgeFlagstone(float2 p, float4 axes, float courses, float coverage, float aa)
{
    float2 tile = courses * float2(dot(p, axes.xy), dot(p, axes.zw));
    float2 cell = floor(tile), q = frac(tile) - .5;
    float2 grain = H22(cell + 13.6);
    q *= lerp(-1.0, 1.0, step(.5, grain));
    // Neighbouring courses can survive together, with an occasional whole slab missing.
    float pocket = VNoise(cell * .36 + 8.13);
    float remaining = coverage + .44 * (pocket - .5) + .28 * (grain.x - .5);
    float intact = smoothstep(.11, .86, remaining);
    float wear = 1.0 - intact;
    float2 chipCentre = (H22(cell + 73.2) - .5) * .53 * wear;
    float2 v = q - chipCentre;
    v.x += (grain.y - .5) * v.y * 1.35 * wear;
    float2 radius = lerp(float2(.055, .042) + grain * .068, float2(.54, .54), intact);
    float rag = (EdgeDryBrush(tile * 4.1) - .5) * .21 * wear;
    float side = min(radius.x - abs(v.x), radius.y - abs(v.y));
    float corner = .30 + .79 * intact + (grain.y - .5) * .19 - v.x - v.y;
    float lip = aa + .03 * wear;
    float body = smoothstep(-lip, lip, min(side, corner) + rag);
    float split = abs(q.y - (.38 + grain.x * .41) * q.x - (grain.y - .5) * .44 + .5 * rag);
    float cleft = smoothstep(.10 * wear * wear - lip, .10 * wear * wear + lip, split + .045 * intact);
    body *= lerp(1.0, cleft, smoothstep(.26, .72, grain.y)) * smoothstep(.085, .36, remaining);
    float2 chip = q - float2(.35, -.26) + (grain - .5) * .19;
    float chipSize = .20 * wear * smoothstep(.035, .38, coverage) * (.45 + .55 * grain.y);
    float fragment = smoothstep(-aa, aa, chipSize - abs(chip.x) - 1.55 * abs(chip.y + .35 * chip.x));
    return saturate(max(body, fragment));
}

void EdgeMaterials(float2 p, float4 wa, float4 wb, float aa, inout float w[6], inout float total)
{
    // Leave the arena portals to the landmark paving and the established region blend.
    float outsideArena = 1.0 - smoothstep(.01, .40, wb.g);
    float snowSand = w[3] + w[4];
    if (_RegionEdgeArt.w > 0.0 && wa.a > .005 && wb.r > .005 && snowSand > .005)
    {
        float ratio = w[3] / snowSand;
        float2 wind = float2(p.x + .34 * p.y, p.y - .23 * p.x);
        float cut = .18 + .46 * EdgeDryBrush(wind * float2(.54, 1.06))
                         + .18 * EdgeDryBrush(wind * float2(2.0, 3.7) + 13.4);
        float crest = smoothstep(cut - .105, cut + .105, ratio);
        float snow = lerp(ratio, crest, _RegionEdgeArt.w * outsideArena);
        w[3] = snowSand * snow;
        w[4] = snowSand - w[3];
    }

    float previous = w[1] / total;
    float strength = _RegionEdgeArt.z * _DryPaint[1].y * outsideArena;
    if (strength > 0.0 && _DryPaint[1].z > 1.0 && wa.g > .012 && wa.g < .985 && previous < .999)
    {
        float stone = EdgeFlagstone(p, _DryPaintUV[1], _DryPaint[1].z, wa.g, aa);
        float border = smoothstep(.012, .12, wa.g) * (1.0 - smoothstep(.80, .985, wa.g));
        // Dust fills the mortar and partly buries exposed chips. Leave the loose pigment in
        // those gaps so a bright neighbouring material cannot make a hard geometric outline.
        float exposure = .44 + .45 * smoothstep(.09, .78, wa.g);
        float wanted = lerp(previous, stone, strength * border * exposure);
        float other = (1.0 - wanted) / max(1e-4, total - w[1]);
        [unroll] for (int n = 0; n < 6; n++) w[n] *= other;
        w[1] = wanted;
        total = 1.0;
    }
}

// A handful of angular dark brush marks between the last upright basalt rocks and
// bare ground. Radius and abundance fall independently as the volcanic material runs out.
float3 EdgeBasaltChips(float3 ground, float3 ash, float2 p, float fire, float arena, float aa)
{
    float band = smoothstep(.018, .15, fire) * (1.0 - smoothstep(.80, .99, fire));
    if (band * _RegionEdgeArt.y <= .002 || arena > .15) return ground;
    float2 lattice = float2(p.x + .13 * p.y, p.y - .13 * p.x) * 1.12;
    float2 cell = floor(lattice), seed = H22(cell + 42.73);
    float radius = lerp(.043, .21, smoothstep(.01, .87, fire)) * (.58 + .78 * seed.y);
    float2 v = frac(lattice) - (.20 + .60 * seed);
    v.x += v.y * (1.4 * seed.x - .7);
    v.y *= 1.04 + .81 * seed.y;
    float rim = max(abs(v.x) * .84 + abs(v.y) * .56,
                    max(.94 * abs(v.y - .47 * v.x), .81 * abs(v.x + .51 * v.y))) - radius;
    rim += radius * .21 * (EdgeDryBrush(p * 9.1) - .5);
    float pocket = VNoise(p * .39 + 3.77);
    float scatter = smoothstep(.66, .73, seed.x + .22 * fire + .24 * (pocket - .50));
    float lip = aa + .022;
    float coverage = (1.0 - smoothstep(-lip, lip, rim)) * band * scatter * _RegionEdgeArt.y;
    float paper = .74 + .38 * EdgeDryBrush(p * 8.0 + cell);
    float3 stone = (float3(.054, .043, .042) + ash * .42) * paper;
    float edge = smoothstep(-.052, .0, rim);
    return lerp(ground, stone * (1.0 - .21 * edge), coverage * (.30 + .49 * fire));
}

// A dry straw wash on the actual low-grass mask, before any snow, hazard, shadow or light.
float3 EdgeWinterGrass(float3 col, float grass, float cold, float2 p)
{
    float weather = grass * smoothstep(.012, .58, cold) * _RegionEdgeArt.x;
    if (weather <= .001) return col;
    float grain = EdgeDryBrush(float2(p.x * 4.2 + p.y, p.y * 2.2));
    float3 straw = lerp(float3(.123, .090, .060), float3(.30, .24, .156), grain);
    return lerp(col, straw, .82 * weather);
}
#endif
