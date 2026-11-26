// Seamless surface-locked Deferred snow layer for characters/creatures (rendered via CommandBuffer.DrawRenderer at
// CameraEvent.AfterGBuffer). Uses ZTest Equal with zero vertex extrusion or depth bias so only visible GBuffer pixels
// are shaded, preserving AO (GBuffer0.a), smoothness (GBuffer1.a), world normals (GBuffer2), shadows, light probes,
// and infusion emission (GBuffer3). Driven by global _MorgottCreatureSnow (0..1) so one shared material renders all
// characters without clones or MaterialPropertyBlock churn.
Shader "Morgott/CreatureSnow"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DEFERRED"
            Tags { "LightMode" = "Deferred" }
            Cull Back
            ZWrite Off
            ZTest Equal
            Offset 0, 0
            Blend 0 SrcAlpha OneMinusSrcAlpha
            ColorMask RGB 0
            Blend 1 SrcAlpha OneMinusSrcAlpha
            ColorMask RGB 1
            ColorMask 0 2
            ColorMask 0 3

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            // Global float set via CommandBuffer.SetGlobalFloat per character (kept out of Properties so material defaults never mask it).
            float _MorgottCreatureSnow;

            struct appdata
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 wn       : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float3 localPos : TEXCOORD2;
            };

            float3 SafeNormalize(float3 v, float3 fallback)
            {
                float d = dot(v, v);
                return d > 1e-12 ? v * rsqrt(d) : fallback;
            }

            float Hash2(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash2(i);
                float b = Hash2(i + float2(1.0, 0.0));
                float c = Hash2(i + float2(0.0, 1.0));
                float d = Hash2(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Hash3(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash3(i);
                float b = Hash3(i + float3(1.0, 0.0, 0.0));
                float c = Hash3(i + float3(0.0, 1.0, 0.0));
                float d = Hash3(i + float3(1.0, 1.0, 0.0));
                float e = Hash3(i + float3(0.0, 0.0, 1.0));
                float f1 = Hash3(i + float3(1.0, 0.0, 1.0));
                float g = Hash3(i + float3(0.0, 1.0, 1.0));
                float h = Hash3(i + float3(1.0, 1.0, 1.0));
                return lerp(
                    lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y),
                    lerp(lerp(e, f1, f.x), lerp(g, h, f.x), f.y),
                    f.z);
            }

            float3 WorldToMetricLocal(float3 wp)
            {
                float3 objOrigin = float3(unity_ObjectToWorld[0][3], unity_ObjectToWorld[1][3], unity_ObjectToWorld[2][3]);
                float3 rel = wp - objOrigin;
                float3 ax = SafeNormalize(float3(unity_ObjectToWorld[0][0], unity_ObjectToWorld[1][0], unity_ObjectToWorld[2][0]), float3(1.0, 0.0, 0.0));
                float3 ay = SafeNormalize(float3(unity_ObjectToWorld[0][1], unity_ObjectToWorld[1][1], unity_ObjectToWorld[2][1]), float3(0.0, 1.0, 0.0));
                float3 az = SafeNormalize(float3(unity_ObjectToWorld[0][2], unity_ObjectToWorld[1][2], unity_ObjectToWorld[2][2]), float3(0.0, 0.0, 1.0));
                return float3(dot(rel, ax), dot(rel, ay), dot(rel, az));
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = SafeNormalize(mul(v.normal, (float3x3)unity_WorldToObject), float3(0.0, 1.0, 0.0));
                o.uv = v.texcoord.xy;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                o.localPos = WorldToMetricLocal(wp);
                return o;
            }

            void frag(
                v2f i,
                out half4 gb0 : SV_Target0,
                out half4 gb1 : SV_Target1)
            {
                float snow = saturate(_MorgottCreatureSnow);
                if (snow <= 0.01) discard;

                float up = SafeNormalize(i.wn, float3(0.0, 1.0, 0.0)).y;

                // Primary grain noise locked to mesh UVs; falls back to metric localPos when UVs are absent/collapsed.
                float rawUvN = ValueNoise2(i.uv * 18.0) * 0.68 + ValueNoise2(i.uv * 6.0) * 0.32;
                float nUV = saturate((rawUvN - 0.18) / 0.64);
                float rawPosN = ValueNoise3(i.localPos * 10.0) * 0.68 + ValueNoise3(i.localPos * 3.5) * 0.32;
                float nPos = saturate((rawPosN - 0.22) / 0.56);
                float uvDensity = dot(fwidth(i.uv), 1.0) / max(length(fwidth(i.localPos)), 1e-5);
                float n = lerp(nPos, nUV, smoothstep(0.05, 0.25, uvDensity));

                float minUp = lerp(0.82, 0.42, pow(snow, 0.7));
                float feather = 0.24;
                float rawMask = smoothstep(minUp - feather * 0.5, minUp + feather * 0.5, up + (n - 0.5) * 0.22);
                float alpha = saturate(rawMask * smoothstep(0.02, 0.95, snow) * 0.90);
                if (alpha < 0.005) discard;

                float3 snowAlbedo = float3(0.76, 0.81, 0.88) * (0.94 + 0.06 * n);
                gb0 = half4(snowAlbedo, alpha);
                gb1 = half4(0.04, 0.045, 0.055, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
