Shader "Roguehalla/DrawnBow"
{
    Properties
    {
        [PerRendererData] _MainTex("Canonical bow", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "RhBowChord.hlsl"

            struct Input
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _RhBowChord;
            float _RhBowChordWidth;

            Output Vert(Input input)
            {
                Output output = (Output)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                output.positionCS = TransformObjectToHClip(UnityFlipSprite(input.positionOS, unity_SpriteProps.xy));
                output.color = input.color * unity_SpriteColor;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Output input) : SV_Target
            {
                half4 paint = input.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                paint.a *= RhStraightBowStringCoverage(input.uv, _RhBowChord, _RhBowChordWidth);
                return paint;
            }
            ENDHLSL
        }
    }
}
