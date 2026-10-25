// A sprite drawn as if etched / chalked onto the table (URP), like the writing on
// an Inscryption game board. The picture is turned black and white: its brightness
// is pushed through a contrast curve and mapped from _Shadow (dark parts) to _Ink
// (light parts), so any coloured sticker comes out as one consistent two-tone mark.
// A fine world-space grain eats into it, so it reads as scratched into the wood
// rather than printed on top; _Opacity lets the table show through.
//
// Used by FloodStageBadge (loaded from Resources). Works on a SpriteRenderer: the
// renderer's colour only fades it (alpha), it never tints it.
Shader "HexFluid/EtchedSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Ink ("Ink (light parts)", Color) = (0.90, 0.87, 0.80, 1)
        _Shadow ("Shadow (dark parts)", Color) = (0.06, 0.05, 0.05, 1)
        _Contrast ("Contrast", Range(0.5, 6)) = 2.2
        _Threshold ("Mid Point", Range(0, 1)) = 0.5
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _Grain ("Scratch Grain", Range(0, 1)) = 0.35
        _GrainScale ("Grain Scale (per world unit)", Float) = 90
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Etched"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Ink;
                half4 _Shadow;
                half _Contrast;
                half _Threshold;
                half _Opacity;
                half _Grain;
                float _GrainScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                // Black and white: brightness through a contrast curve, mapped onto two tones.
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                lum = saturate((lum - _Threshold) * _Contrast + 0.5);
                half3 col = lerp(_Shadow.rgb, _Ink.rgb, lum);

                // Grain in world space (so it stays put on the wood as the mark wobbles):
                // fine speckle plus streaks along the table, like scratches.
                float2 g = i.positionWS.xz * _GrainScale;
                float speck = Hash(floor(g));
                float streak = Hash(floor(float2(g.x * 0.12, g.y)));
                half grain = saturate(0.55 + 0.45 * speck * 0.6 + 0.45 * streak * 0.4);
                half bite = lerp(1.0, grain, _Grain);

                half a = tex.a * _Opacity * bite * i.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
