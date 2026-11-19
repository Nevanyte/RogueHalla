// Lit sprite for region props: the regular 2D Light2D result, multiplied by the shared one-sun term (RhSun.hlsl) so props agree
// with the ground and walls. _MainTex comes from the SpriteRenderer.
Shader "Roguehalla/RegionSpriteLit"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _StormInkResponse("Ink response (original, matte, flame)", Float) = 0
        [PerRendererData] _StormFlameUV("Atlas flame centre and inverse radius", Vector) = (0,0,0,0)
        _Ndl("Sun facing", Float) = 0.55
        [PerRendererData] _WetRootContact("Wet root pigment", Float) = 0
        [HideInInspector] _LayeredCanopy("Overlapping ink wash", Float) = 0
    }
    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half2 lightingUV : TEXCOORD1; float2 world : TEXCOORD2; };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            #if USE_SHAPE_LIGHT_TYPE_0
            SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
            SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
            SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
            SHAPE_LIGHT(3)
            #endif

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            #include "RhSun.hlsl"
            #include "RhLocalLights.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Ndl;
                half _StormInkResponse;
                float4 _StormFlameUV;
                half _LayeredCanopy;
                half _WetRootContact;
            CBUFFER_END

            float _RhSwayV;   // atlas V below which the art is wind-capable (RegionPropArt packs it first); 0 = no sway

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                SetUpSpriteInstanceProperties();
                float3 wp = TransformObjectToWorld(v.positionOS);
                // Wind: a gusting shear that grows with height above the foot, and a soft push away from presented actors (grass and bushes part around them).
                [branch] if (_RhWind.z > 0.001 && v.uv.y < _RhSwayV)
                {
                    float2 foot = unity_ObjectToWorld._m03_m13;
                    float h = max(wp.y - foot.y, 0.0);
                    float w = saturate(h / 1.7); w *= w;
                    float2 ft = float2(foot.x, -foot.y / _RhMapInfo.z);
                    float gust = 0.5 + 0.5 * sin(dot(ft, _RhWind.xy) * 0.42 - _Time.y * _RhWind.w);
                    float gust2 = sin(_Time.y * 1.9 + ft.x * 2.3 + ft.y * 1.7 + h * 1.6);
                    float2 push = 0;
                    int na = (int)_RhActorN.x;
                    [loop] for (int a = 0; a < 8; a++)
                    {
                        if (a >= na) break;
                        float2 d = ft - _RhActors[a].xy; float dl = length(d);
                        push += (d / max(dl, 0.05)) * (1.0 - smoothstep(0.15, 1.1, dl));
                    }
                    wp.x += (_RhWind.x * (0.035 + 0.09 * gust) + 0.012 * gust2 + push.x * 0.20) * w * _RhWind.z;
                    wp.y += push.y * -0.10 * w * _RhWind.z;
                }
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv; o.color = v.color * unity_SpriteColor;
                o.world = wp.xy;
                o.lightingUV = half2(ComputeScreenPos(o.positionCS / o.positionCS.w).xy);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 main = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                [branch] if (_WetRootContact > 0.5h)
                {
                    half silt = 1.0h - smoothstep(.035h, .34h, i.uv.y);
                    main.rgb *= lerp(half3(1, 1, 1), half3(.48h, .55h, .47h), silt);
                    main.a *= 1.0h - .25h * silt;
                }
                // The tree keeps its soft silhouette away from combat. Clear overlapping washes at the torsos
                // of the same visible actors already uploaded for ground shadows and foliage.
                [branch] if (_LayeredCanopy > 0.5h && i.color.a < 0.85h)
                {
                    float2 tile = float2(i.world.x, -(i.world.y - 0.62) / _RhMapInfo.z);
                    main.a *= lerp(i.color.a, 1.0h, RhActorClear(tile));
                }
                clip(main.a - 0.004);
                float sunStr; float3 shMul;
                bool painted = _StormInkResponse > .5h;
                half3 pigment = main.rgb;
                main.rgb *= RhSunMul(i.world, _Ndl, sunStr, shMul);
                SurfaceData2D surfaceData;
                InputData2D inputData;
                float3 albedo = main.rgb;
                float2 px = abs(float2(ddx(i.uv.x), ddy(i.uv.y))) + 1e-5;      // derivative outside the light branch (well defined)
                InitializeSurfaceData(main.rgb, main.a, half4(0, 0, 0, 0), half3(0, 0, 1), surfaceData);   // mask 0: see RegionLitMesh.shader
                InitializeInputData(i.uv, i.lightingUV, inputData);
                half4 lit = CombinedShapeLightShared(surfaceData, inputData);
                // Emissive paint: bright, saturated pixels (lantern glass and flame, crystals, vents, glowing fungus) glow a little on their own, so they carry
                // the bloom threshold at night; white snow and pale stone are not saturated and stay as they are.
                [branch] if (_StormInkResponse > 1.5h)
                {
                    // Only the upper flame and lit glass carry light. A gold stone border stays matte.
                    half hot = smoothstep(.30h, .72h, pigment.r - pigment.b) * smoothstep(.50h, .82h, pigment.r);
                    half mask = smoothstep(.62h, .80h, i.uv.y);
                    if (_StormFlameUV.z > 0)
                        mask = 1.0h - smoothstep(.40, 1.25, length((i.uv - _StormFlameUV.xy) * _StormFlameUV.zw));
                    lit.rgb += pigment * (.25h * hot * mask) * main.a;
                }
                else if (!painted && _RhLI.x > .5)
                {
                    float mx = max(albedo.r, max(albedo.g, albedo.b)), mn = min(albedo.r, min(albedo.g, albedo.b));
                    float em = smoothstep(0.62, 0.92, mx) * smoothstep(0.32, 0.62, (mx - mn) / max(mx, 0.01));
                    lit.rgb += albedo * em * 0.55 * main.a;
                }
                [branch] if (_RhLI.x > 0.5)
                {
                    float2 pt = float2(i.world.x, -i.world.y / _RhMapInfo.z);
                    [branch] if (RhAnyLight(pt))
                    {
                        // Pseudo normal from the sprite itself: a dome from its blurred alpha (round trunks, bushes, urns) plus the painted luma.
                        float2 oa = px * 3.0, ol = px * 1.2;
                        float aR = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(oa.x, 0)).a, aL = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(oa.x, 0)).a;
                        float aU = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0, oa.y)).a, aD = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0, oa.y)).a;
                        float2 tilt = float2(aL - aR, aU - aD) * (painted ? .32 : .9);
                        [branch] if (!painted)
                        {
                            float3 lw = float3(.2126, .7152, .0722);
                            float lR = dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(ol.x, 0)).rgb, lw);
                            float lL = dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(ol.x, 0)).rgb, lw);
                            float lU = dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(0, ol.y)).rgb, lw);
                            float lD = dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv - float2(0, ol.y)).rgb, lw);
                            tilt += float2(lL - lR, lU - lD) * 1.4;
                        }
                        float3 dif, spc;
                        RhLocalLights(pt, normalize(float3(tilt, 1.0)), painted ? .018 : .10, 14.0, 0.8, 1.0, 0.0, dif, spc);
                        float hr = RhHeadroom(lit.rgb);
                        float ink = painted ? smoothstep(.012, .16, max(pigment.r, max(pigment.g, pigment.b))) : 1.0;
                        float3 receiver = painted ? pigment : albedo;
                        lit.rgb += (receiver * dif * hr + spc * saturate(hr + .05) * ink) * main.a;
                    }
                }
                lit.rgb = RhFogGrade(i.world, lit.rgb);
                return lit;
            }
            ENDHLSL
        }
    }
}
