// 3D flowing water for the flood (URP), drawn on WaterBody's one continuous mesh.
//
// The flood is planned up front and baked by WaterBody into _FloodMap (object XZ ->
// texel):  R = when the front reaches that point (in tiles),  G = signed distance
// to dry ground (+ in the water),  BA = the current there (object XZ).
// _HexWaterTime is the shared playback clock (WaterSimulation.WaterTime).
//
// Per pixel / vertex:
//   - Front: arrival + world-space wobble vs the clock. A small surge rides the front
//     and the water fills up behind it, as real geometry (vertex displacement).
//   - Waves: gentle swell carried along the current, fading out at the banks.
//   - Ripples: two-phase flowmap detail normals (move with the current, never smear).
//   - Depth from the scene: the sunk tiles show through tinted and refracted, turning
//     deep blue with depth; contact foam where the water meets houses and walls.
//   - Sky reflection (adds, never darkens), glints, foam at banks and the front.
//
// Needs URP "Depth Texture" and "Opaque Texture" on (both are on in PC_RPAsset).
Shader "HexFluid/FlowingWater"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow Tint", Color) = (0.62, 0.95, 0.93, 1)
        _DeepColor ("Deep Color", Color) = (0.04, 0.33, 0.52, 1)
        _Absorption ("Absorption (per unit of depth)", Float) = 9
        _DepthCap ("Depth Cap (hides seams in the floor)", Float) = 0.22
        _FoamColor ("Foam Color", Color) = (0.96, 0.99, 1.0, 1.0)
        _SkyColor ("Sky Reflection Color", Color) = (0.62, 0.80, 0.98, 1.0)
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.45
        _GlintDir ("Glint Light Direction", Vector) = (0.35, 0.85, 0.4, 0)

        [Header(Waves)]
        _WaveHeight ("Wave Height", Float) = 0.022
        _WaveScale ("Wave Scale", Float) = 0.8
        _SurgeHeight ("Front Surge Height", Float) = 0.05
        _FillDrop ("Fill Drop (fresh water sits lower)", Float) = 0.06

        [Header(Current)]
        _FlowSpeed ("Flow Speed (units per second)", Float) = 0.45
        _FlowCycle ("Flow Cycle (seconds)", Float) = 1.6
        _RippleScale ("Ripple Scale", Float) = 2.6
        _NormalStrength ("Ripple Normal Strength", Range(0, 3)) = 0.9

        [Header(Light)]
        _Gloss ("Gloss", Range(8, 512)) = 200
        _SpecStrength ("Specular Strength", Range(0, 3)) = 1.1
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _Refraction ("Refraction Strength", Range(0, 0.2)) = 0.035

        [Header(Foam and Front)]
        _ContactFoamDepth ("Contact Foam Depth", Range(0, 0.3)) = 0.025
        _BankFoamWidth ("Bank Foam Width", Range(0, 0.5)) = 0.025
        _FoamSharpness ("Foam Sharpness", Range(0, 1)) = 0.8
        _EdgeOverhang ("Edge Overhang", Range(0, 0.3)) = 0.015
        _FrontSoftness ("Front Softness (tiles)", Range(0.01, 0.5)) = 0.07
        _FrontWobble ("Front Wobble (tiles)", Range(0, 1)) = 0.35
        _FrontFoamFade ("Front Foam Fade (tiles)", Range(0.02, 1)) = 0.2
        _FilmFade ("Thin Film Fade (tiles)", Range(0.05, 2)) = 0.6

        // Set by WaterBody
        [HideInInspector] _FloodMap ("Flood Map", 2D) = "black" {}
        [HideInInspector] _BankMap ("Bank Map", 2D) = "white" {}
        [HideInInspector] _FloodRect ("Flood Rect", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _Absorption;
                float _DepthCap;
                half4 _FoamColor;
                half4 _SkyColor;
                float _ReflectionStrength;
                float4 _GlintDir;
                float _WaveHeight;
                float _WaveScale;
                float _SurgeHeight;
                float _FillDrop;
                float _FlowSpeed;
                float _FlowCycle;
                float _RippleScale;
                float _NormalStrength;
                float _Gloss;
                float _SpecStrength;
                float _FresnelPower;
                float _Refraction;
                float _ContactFoamDepth;
                float _BankFoamWidth;
                float _FoamSharpness;
                float _EdgeOverhang;
                float _FrontSoftness;
                float _FrontWobble;
                float _FrontFoamFade;
                float _FilmFade;
                float4 _FloodMap_ST;
                float4 _BankMap_ST;
                float4 _FloodRect;
            CBUFFER_END

            TEXTURE2D(_FloodMap);
            SAMPLER(sampler_FloodMap);
            TEXTURE2D(_BankMap);
            SAMPLER(sampler_BankMap);

            // Shared playback clock, set by WaterSimulation (in tiles).
            float _HexWaterTime;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 xzOS : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            // ---- Cheap value noise ----------------------------------------------------
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.07 + 5.2) * 0.30 + Noise(p * 4.31 + 1.7) * 0.15;
            }

            // Two-phase flowmap: two copies of the pattern slide along the current and
            // take turns, each fading out before it would stretch too far.
            float FlowNoise(float2 xz, float2 flow, float scale)
            {
                float cycle = _Time.y / max(_FlowCycle, 0.05);
                float p0 = frac(cycle);
                float p1 = frac(cycle + 0.5);
                float w0 = 1.0 - abs(1.0 - 2.0 * p0);
                float travel = _FlowSpeed * _FlowCycle;
                float a = Fbm((xz - flow * (p0 * travel)) * scale);
                float b = Fbm((xz - flow * (p1 * travel)) * scale + float2(0.37, 0.71));
                return lerp(b, a, w0);
            }

            // ---- Flood map ---------------------------------------------------------------
            float4 Flood(float2 xzOS)
            {
                return SAMPLE_TEXTURE2D_LOD(_FloodMap, sampler_FloodMap, (xzOS - _FloodRect.xy) * _FloodRect.zw, 0);
            }

            float2 CurrentWS(float4 m)
            {
                float3 d = TransformObjectToWorldDir(float3(m.z, 0, m.w), false);
                float len = length(d.xz);
                return len > 1e-3 ? d.xz / len : float2(0, -1);
            }

            float Wobble(float2 xzWS)
            {
                return (Noise(xzWS * 1.7) - 0.5) * _FrontWobble
                     + (Noise(xzWS * 5.3 + 3.1) - 0.5) * _FrontWobble * 0.4;
            }

            // Tiles since the water got here (negative = not yet).
            float Age(float4 m, float2 xzWS)
            {
                return _HexWaterTime - (m.r + Wobble(xzWS));
            }

            // Surface height: swell along the current (calm at the banks), a surge
            // riding the front, and fresh water sitting a little lower as it fills up.
            float Height(float2 xzWS, float2 flow, float age, float bank)
            {
                float swell = (FlowNoise(xzWS, flow, _WaveScale) - 0.5) * 2.0 * _WaveHeight * saturate(bank / 0.3);
                float surge = _SurgeHeight * exp(-max(age, 0.0) / 0.3) * smoothstep(0.0, 0.1, age);
                float fill = -_FillDrop * (1.0 - saturate(age / _FilmFade));
                return swell + surge + fill;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pos = v.positionOS.xyz;
                float4 m = Flood(pos.xz);
                float2 xzWS = TransformObjectToWorld(pos).xz;
                pos.y += Height(xzWS, CurrentWS(m), Age(m, xzWS), m.g);

                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.xzOS = v.positionOS.xz;
                o.eyeDepth = -p.positionVS.z;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 m = Flood(i.xzOS);
                float2 xz = i.positionWS.xz;

                // ---- Where the water is: inside the flood (a hair past it, under the dry
                // tiles), and behind the front.
                float edge = saturate((m.g + _EdgeOverhang) / max(_EdgeOverhang, 1e-3));
                float age = Age(m, xz);
                float covered = smoothstep(0.0, _FrontSoftness, age) * edge;
                clip(covered - 0.002);
                float film = saturate(age / _FilmFade);
                float frontFoam = exp(-max(age, 0.0) / _FrontFoamFade);

                // ---- Surface normal: waves + flowing ripples.
                float2 flow = CurrentWS(m);
                const float e = 0.04;
                float h0 = Height(xz, flow, age, m.g);
                float hx = Height(xz + float2(e, 0), flow, age, m.g);
                float hz = Height(xz + float2(0, e), flow, age, m.g);
                float r0 = FlowNoise(xz, flow, _RippleScale);
                float rx = FlowNoise(xz + float2(e, 0), flow, _RippleScale);
                float rz = FlowNoise(xz + float2(0, e), flow, _RippleScale);
                float rippleK = 0.08 * _NormalStrength;
                float3 normalWS = normalize(float3(-(hx - h0) / e + (r0 - rx) / e * rippleK, 1.0,
                                                   -(hz - h0) / e + (r0 - rz) / e * rippleK));

                // ---- What's under the water, from the scene: depth, refraction.
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float thick = max(0.0, sceneZ - i.eyeDepth);

                float2 ruv = suv + normalWS.xz * _Refraction * saturate(thick * 6.0);
                float sceneZR = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (sceneZR < i.eyeDepth) { ruv = suv; sceneZR = sceneZ; }   // don't refract things in front of the water
                float thickR = max(0.0, sceneZR - i.eyeDepth);
                half3 under = SampleSceneColor(ruv);

                float vertical = max(0.25, viewDir.y);                    // view-ray thickness -> water depth
                float depth = min(thickR * vertical, _DepthCap) * lerp(0.35, 1.0, film);
                float absorb = 1.0 - exp(-depth * _Absorption);
                half3 col = lerp(under * _ShallowColor.rgb, _DeepColor.rgb, absorb);

                // ---- Light: stylised, independent of the scene having a sun.
                Light mainLight = GetMainLight();
                float3 glintDir = normalize(_GlintDir.xyz);
                col *= lerp(0.88, 1.06, saturate(dot(normalWS, glintDir)));

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                half3 env = GlossyEnvironmentReflection(reflect(-viewDir, normalWS), 0.08, 1.0);
                col += max(env, _SkyColor.rgb * 0.5) * (0.06 + fresnel) * _ReflectionStrength;
                float spec = pow(saturate(dot(normalWS, normalize(glintDir + viewDir))), _Gloss) * _SpecStrength
                           + pow(saturate(dot(normalWS, normalize(mainLight.direction + viewDir))), _Gloss) * _SpecStrength
                             * Luminance(mainLight.color);
                col += spec;

                // ---- Foam: where it touches things, along the banks, and at the front.
                float breakup = 0.45 + 0.55 * Noise(xz * 9.0 - flow * _Time.y * 1.5);
                float contact = 1.0 - smoothstep(0.0, _ContactFoamDepth, thick * vertical);
                // Bank foam only where the water meets a dry tile on the board, not the board's outside.
                float bankDist = SAMPLE_TEXTURE2D_LOD(_BankMap, sampler_BankMap, (i.xzOS - _FloodRect.xy) * _FloodRect.zw, 0).r;
                float bankFoam = 1.0 - smoothstep(0.0, _BankFoamWidth, bankDist);
                float front = frontFoam * (0.55 + 0.45 * Noise(xz * 11.0 + flow * _Time.y));
                float foam = saturate((contact * 0.8 + bankFoam * 0.6) * breakup + front);
                // Crisp foam: squeeze the soft gradient into a thin, clean line.
                float k = _FoamSharpness * 0.45;
                foam = smoothstep(0.5 - (0.5 - k), 0.5 + (0.5 - k), foam);
                col = lerp(col, _FoamColor.rgb, foam * _FoamColor.a);

                col = MixFog(col, i.fogFactor);
                return half4(col, covered);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
