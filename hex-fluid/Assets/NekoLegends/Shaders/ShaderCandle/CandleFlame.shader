Shader "Neko Legends/Flame/Candle"
{
    Properties
    {
        [Header(Flicker Settings)]
        _Speed          ("Flicker Speed", Float) = 1
        [Toggle] _UseWorldPositionSeed ("Use World‑Pos Flicker Seed", Float) = 1
        _FlickerSeed    ("Manual Flicker Seed",  Float) = 0

        [Space]
        [Header(Flame Shape)]
        _ShapeScale     ("Scale",                Float)      = 3
        _ShapeAspect    ("Aspect (X,Y)",         Vector)     = (1, 1, 0, 0)
        _ShapeOffset    ("Offset (X,Y)",         Vector)     = (0, 0, 0, 0)
        _AlmondFactor   ("Almond Factor",        Range(0,2)) = 0.5

        [Space]
        [Header(Colors)]
        _ColorA         ("Base Color",           Color)      = (1,0.5,0,1)
        _ColorB         ("Tip Color",            Color)      = (1,1,0.6,1)

        [Space]
        [Header(Glow)]
        _BlurSize       ("Blur Offset",          Float)      = 0.003
        _Intensity      ("Flame Intensity",      Float)      = 1

    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 posOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 posHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float _Speed;
                float _FlickerSeed;
                float _UseWorldPositionSeed;

                float _ShapeScale;
                float4 _ShapeAspect;
                float4 _ShapeOffset;
                float  _AlmondFactor;

                float4 _ColorA;
                float4 _ColorB;

                float _BlurSize;
                float _Intensity;
            CBUFFER_END

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898,78.233))) * 43758.5453); }
            float noise2D(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f*f*(3.0-2.0*f);
                float n00 = hash(i), n10 = hash(i + float2(1,0));
                float n01 = hash(i + float2(0,1)), n11 = hash(i + 1);
                return lerp( lerp(n00,n10,u.x), lerp(n01,n11,u.x), u.y );
            }
            float fbm(float2 p)
            {
                float sum = 0, amp = 0.5;
                [unroll(2)]
                for(int i=0;i<2;i++){ sum += amp*noise2D(p); p*=2; amp*=0.5; }
                return sum;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posHCS = TransformObjectToHClip(IN.posOS.xyz);
                OUT.uv      = IN.uv;
                return OUT;
            }

            // build color+alpha mask
            float4 FlameColor(float2 uv, float t)
            {
                // shift & warp UV
                float2 cUV = uv - (0.5 + _ShapeOffset.xy);
                float n1 = fbm(cUV * 2.5 + float2(t*0.3, t*0.5));
                float n2 = fbm(cUV * 3.0 - float2(t*0.6, t*0.4));
                float2 off = (float2(n1,n2)-0.5) * 0.3;
                float2 wUV = cUV + off;

                // aspect+scale
                float2 sc = wUV * _ShapeAspect.xy * _ShapeScale;

                // circle mask + almond tweak
                float m = saturate(1 - length(sc));
                float pf = lerp(1, 3, _AlmondFactor);
                m = pow(m, pf);

                // lerp base→tip
                float3 col = lerp(_ColorA.rgb, _ColorB.rgb, m);
                return float4(col, m);
            }

            // 5‑tap blur
            float4 Blur5(float2 uv, float t, float off)
            {
                float4 s = FlameColor(uv, t);
                float2 o = float2(off,0);
                s += FlameColor(uv + o.xy, t);
                s += FlameColor(uv - o.xy, t);
                s += FlameColor(uv + o.yx, t);
                s += FlameColor(uv - o.yx, t);
                return s * (1.0/5.0);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float seed = _FlickerSeed;

                if (_UseWorldPositionSeed > 0.5)
                {
                    // reliable world‑pos lookup in URP
                    float3 worldPos = unity_ObjectToWorld._m03_m13_m23;
                    seed = frac(sin(dot(worldPos, float3(12.9898,78.233,37.719))) * 43758.5453);
                }

                float t = (_Time.y + seed) * _Speed;
                float4 c = Blur5(IN.uv, t, _BlurSize);
                c.rgb *= _Intensity;
                return c;
            }
            ENDHLSL
        }
    }
}
