// Volumetric 3D top-only Deferred snow cap for characters/creatures (rendered via CommandBuffer.DrawRenderer at
// CameraEvent.AfterGBuffer). Replaces vanilla _SnowCover (tangent-space Z painting) with world-upward extrusion and
// body-locked metric 3D noise (scale-invariant on 0.01 FBX bones). Driven by global _MorgottCreatureSnow (0..1) so
// one shared material renders all characters without clones or MaterialPropertyBlock churn.
// Linear colour space output + manual linear SH ambient (unity/ builds as Gamma while Valheim runs Linear + HDR;
// matches Morgott/Snow and PieceDeferred.hlsl). Stencil 192 (0xC0) marks extruded silhouette pixels as non-background
// and non-lightmapped for ToonDeferredShading2017.
Shader "Morgott/CreatureSnow"
{
    Properties
    {
        _SnowCover ("Snow Cover", Range(0, 1)) = 0
        _MaxThickness ("Max Thickness (m)", Range(0, 0.06)) = 0.024
        _NoiseScale ("Noise Scale", Float) = 6.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DEFERRED"
            Tags { "LightMode" = "Deferred" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Offset -1, -1
            Stencil
            {
                Ref 192
                WriteMask 192
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile ___ UNITY_HDR_ON
            #include "UnityCG.cginc"

            // Global float set via CommandBuffer.SetGlobalFloat per character (kept out of Properties so material defaults never mask it).
            float _MorgottCreatureSnow;
            float _SnowCover;
            float _MaxThickness;
            float _NoiseScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 wn       : TEXCOORD0;
                float3 wp       : TEXCOORD1;
                float3 localPos : TEXCOORD2;
                float  snow     : TEXCOORD3;
            };

            float3 SafeNormalize(float3 v, float3 fallback)
            {
                float d = dot(v, v);
                return d > 1e-12 ? v * rsqrt(d) : fallback;
            }

            float Hash3(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // 3D trilinear value noise + analytic gradient in noise-cell units: (value, d/dx, d/dy, d/dz).
            float4 NoiseGrad3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                float3 u = f * f * (3.0 - 2.0 * f);
                float3 du = 6.0 * f * (1.0 - f);

                float a = Hash3(i + float3(0.0, 0.0, 0.0));
                float b = Hash3(i + float3(1.0, 0.0, 0.0));
                float c = Hash3(i + float3(0.0, 1.0, 0.0));
                float d = Hash3(i + float3(1.0, 1.0, 0.0));
                float e = Hash3(i + float3(0.0, 0.0, 1.0));
                float f1 = Hash3(i + float3(1.0, 0.0, 1.0));
                float g = Hash3(i + float3(0.0, 1.0, 1.0));
                float h = Hash3(i + float3(1.0, 1.0, 1.0));

                float k0 = a;
                float k1 = b - a;
                float k2 = c - a;
                float k3 = e - a;
                float k4 = a - b - c + d;
                float k5 = a - c - e + g;
                float k6 = a - b - e + f1;
                float k7 = -a + b + c - d + e - f1 - g + h;

                float val = k0 + k1 * u.x + k2 * u.y + k3 * u.z
                          + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x
                          + k7 * u.x * u.y * u.z;
                float3 grad = du * float3(
                    k1 + k4 * u.y + k6 * u.z + k7 * u.y * u.z,
                    k2 + k5 * u.z + k4 * u.x + k7 * u.z * u.x,
                    k3 + k6 * u.x + k5 * u.y + k7 * u.x * u.y
                );
                return float4(val, grad);
            }

            void ObjectAxes(out float3 ax, out float3 ay, out float3 az)
            {
                ax = SafeNormalize(float3(unity_ObjectToWorld[0][0], unity_ObjectToWorld[1][0], unity_ObjectToWorld[2][0]), float3(1.0, 0.0, 0.0));
                ay = SafeNormalize(float3(unity_ObjectToWorld[0][1], unity_ObjectToWorld[1][1], unity_ObjectToWorld[2][1]), float3(0.0, 1.0, 0.0));
                az = SafeNormalize(float3(unity_ObjectToWorld[0][2], unity_ObjectToWorld[1][2], unity_ObjectToWorld[2][2]), float3(0.0, 0.0, 1.0));
            }

            float3 WorldToMetricLocal(float3 wp)
            {
                float3 objOrigin = float3(unity_ObjectToWorld[0][3], unity_ObjectToWorld[1][3], unity_ObjectToWorld[2][3]);
                float3 rel = wp - objOrigin;
                float3 ax, ay, az;
                ObjectAxes(ax, ay, az);
                return float3(dot(rel, ax), dot(rel, ay), dot(rel, az));
            }

            v2f vert(appdata v)
            {
                v2f o;
                float snow = saturate(max(_MorgottCreatureSnow, _SnowCover));
                if (snow <= 0.001)
                {
                    o.pos = float4(2.0, 2.0, 0.0, 1.0);
                    o.wn = float3(0.0, 1.0, 0.0);
                    o.wp = 0.0;
                    o.localPos = 0.0;
                    o.snow = 0.0;
                    return o;
                }

                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 wn = SafeNormalize(mul(v.normal, (float3x3)unity_WorldToObject), float3(0.0, 1.0, 0.0));
                float3 localMetricPos = WorldToMetricLocal(wp);

                float vertNoise = NoiseGrad3(localMetricPos * _NoiseScale).x;
                float upExt = smoothstep(0.42, 0.82, wn.y);
                float thick = _MaxThickness * snow * upExt * (0.65 + 0.35 * vertNoise);
                wp += wn * thick;

                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                o.wn = wn;
                o.wp = wp;
                o.localPos = localMetricPos;
                o.snow = snow;
                return o;
            }

            void frag(
                v2f i,
                out half4 gb0 : SV_Target0,
                out half4 gb1 : SV_Target1,
                out half4 gb2 : SV_Target2,
                out half4 em  : SV_Target3)
            {
                float snow = saturate(i.snow);
                clip(snow - 0.001);

                float3 wn = SafeNormalize(i.wn, float3(0.0, 1.0, 0.0));
                float3 p = i.localPos * _NoiseScale;
                float4 ng1 = NoiseGrad3(p);
                float4 ng2 = NoiseGrad3(p * 2.13 + 7.3);
                float rawN = ng1.x * 0.65 + ng2.x * 0.35;
                float n = saturate((rawN - 0.25) / 0.50);

                // Strict upward-facing cap: snow=1 covers ~0.56 +- 0.11 (wn.y < 0.45 is 100% clipped);
                // lower snow raises the threshold so only the flattest tops catch patches first.
                float thresh = lerp(1.12, 0.56, pow(snow, 0.55));
                float edge = wn.y - thresh + (n - 0.5) * 0.22;
                clip(edge);

                // Powdery snow micro-relief by projecting noise gradient onto the local tangent plane.
                float3 gradLocal = ng1.yzw * 0.65 + ng2.yzw * (0.35 * 2.13);
                float3 ax, ay, az;
                ObjectAxes(ax, ay, az);
                float3 gradWS = gradLocal.x * ax + gradLocal.y * ay + gradLocal.z * az;
                float3 gradTan = gradWS - wn * dot(gradWS, wn);
                float3 normalWS = SafeNormalize(wn - gradTan * 0.16, wn);

                float microNoise = NoiseGrad3(i.localPos * (_NoiseScale * 6.5)).x;
                float3 viewDir = SafeNormalize(_WorldSpaceCameraPos - i.wp, float3(0.0, 1.0, 0.0));
                float glintNoise = NoiseGrad3(i.localPos * 42.0 + viewDir * 1.4).x;
                float sparkle = pow(saturate((glintNoise - 0.55) / 0.45), 6.0);

                float rimAO = lerp(0.88, 1.0, smoothstep(0.0, 0.05, edge));
                half3 albedo = half3(0.74, 0.79, 0.86) * (0.94 + 0.06 * microNoise) * rimAO;
                albedo += half3(0.08, 0.09, 0.10) * sparkle;

                half3 specColor = half3(0.05, 0.055, 0.065) + half3(0.14, 0.16, 0.18) * sparkle;
                half smoothness = saturate(lerp(0.28, 0.38, microNoise) + sparkle * 0.22);

                #if UNITY_LIGHT_PROBE_PROXY_VOLUME
                    half3 sh1 = (unity_ProbeVolumeParams.x == 1.0)
                        ? SHEvalLinearL0L1_SampleProbeVolume(half4(normalWS, 1.0), i.wp)
                        : SHEvalLinearL0L1(half4(normalWS, 1.0));
                #else
                    half3 sh1 = SHEvalLinearL0L1(half4(normalWS, 1.0));
                #endif
                half3 ambient = max(0.0h, sh1 + SHEvalLinearL2(half4(normalWS, 1.0)));
                half3 emissive = albedo * ambient;
                #ifndef UNITY_HDR_ON
                    emissive = exp2(-emissive);
                #endif

                gb0 = half4(albedo, 1.0);
                gb1 = half4(specColor, smoothness);
                gb2 = half4(normalWS * 0.5 + 0.5, 1.0);
                em = half4(emissive, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
