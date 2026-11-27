// Seamless surface-locked Deferred snow layer for characters/creatures (rendered via CommandBuffer.DrawRenderer at
// CameraEvent.BeforeReflections). Uses ZTest Equal with zero vertex extrusion or depth bias so only visible GBuffer
// pixels are shaded, blending SV_Target2 (CameraTarget/GBuffer3 trilight ambient) while preserving AO (GBuffer0.a),
// smoothness (GBuffer1.a), world normals (GBuffer2), shadows, and stencil. Driven by global _MorgottCreatureSnow (0..1)
// so one shared material renders all characters without clones or MaterialPropertyBlock churn.
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
            Blend 2 SrcAlpha OneMinusSrcAlpha
            ColorMask RGB 2
            ColorMask 0 3

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #pragma multi_compile ___ UNITY_HDR_ON
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

            float3 EvalTrilightAmbient(float3 wn)
            {
                float3 sky = unity_AmbientSky.rgb;
                float3 eq  = unity_AmbientEquator.rgb;
                float3 gnd = unity_AmbientGround.rgb;
                return wn.y >= 0.0 ? lerp(eq, sky, saturate(wn.y)) : lerp(eq, gnd, saturate(-wn.y));
            }

            void frag(
                v2f i,
                out half4 gb0 : SV_Target0,
                out half4 gb1 : SV_Target1,
                out half4 gb2_em : SV_Target2)
            {
                float snow = saturate(_MorgottCreatureSnow);
                if (snow <= 0.01) discard;

                float3 wn = SafeNormalize(i.wn, float3(0.0, 1.0, 0.0));
                float up = wn.y;

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
                float alpha = saturate(rawMask * pow(saturate((snow - 0.02) / 0.93), 0.65) * 0.94);
                if (alpha < 0.005) discard;

                float3 snowAlbedo = float3(0.76, 0.81, 0.88) * (0.94 + 0.06 * n);
                float3 emCol = snowAlbedo * EvalTrilightAmbient(wn) * 0.90;
                #ifndef UNITY_HDR_ON
                    emCol = exp2(-emCol);
                #endif
                gb0 = half4(snowAlbedo, alpha);
                gb1 = half4(0.04, 0.045, 0.055, alpha);
                gb2_em = half4(emCol, alpha);
            }
            ENDCG
        }

        // Marks visible creature/character pixels in stencil bit 2 (0x04) so Morgott/WorldSeason skips them.
        Pass
        {
            Name "CreatureStencilMark"
            Tags { "LightMode" = "Deferred" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0
            Stencil
            {
                Ref 4
                WriteMask 4
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        // Real 3D puffed snow cap on upward surfaces, smoothly tucked inside the mesh on sides/underside
        // so surface snow transitions seamlessly into a volumetric silhouette without any frag clip() seams.
        // Driven by global floats (_MorgottCapHeight, _MorgottCapWorldNoise) for both 2.2 cm character caps
        // and world-continuous roof snow blankets up to 18 cm across adjacent roof blocks.
        Pass
        {
            Name "CreatureSnowCap"
            Tags { "LightMode" = "Deferred" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Offset 0, 0
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
            #pragma target 4.0
            #pragma multi_compile ___ UNITY_HDR_ON
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _MorgottCreatureSnow;
            float _MorgottCapHeight;
            float _MorgottCapWorldNoise;

            // Per-instance snow override for CommandBuffer.DrawMeshInstanced; falls back to global _MorgottCreatureSnow when unset (<= 0).
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _MorgottCreatureSnowInst)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 wn       : TEXCOORD0;
                float2 uv       : TEXCOORD1;
                float3 localPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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

            // Trilinear value noise + analytic gradient in noise-cell units: (value, d/dx, d/dy, d/dz).
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
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                bool worldMode = _MorgottCapWorldNoise > 0.5;
                float snowRaw = _MorgottCreatureSnow;
                #ifdef UNITY_INSTANCING_ENABLED
                    float snowInst = UNITY_ACCESS_INSTANCED_PROP(Props, _MorgottCreatureSnowInst);
                    if (snowInst > 0.0) snowRaw = snowInst;
                #endif
                float snow = saturate(snowRaw);
                float capAmt = worldMode ? smoothstep(0.08, 1.0, snow) : smoothstep(0.30, 1.0, snow);
                if (capAmt <= 0.001)
                {
                    o.pos = float4(0.0, 0.0, 0.0, 0.0);
                    o.wn = float3(0.0, 1.0, 0.0);
                    o.uv = float2(0.0, 0.0);
                    o.localPos = float3(0.0, 0.0, 0.0);
                    return o;
                }

                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 wn = SafeNormalize(mul(v.normal, (float3x3)unity_WorldToObject), float3(0.0, 1.0, 0.0));
                float3 localPos = WorldToMetricLocal(wp);

                float3 driftPos = worldMode ? (wp * 0.65) : (localPos * 6.5);
                float vertNoise = saturate((NoiseGrad3(driftPos).x - 0.20) / 0.60);
                float upMask = worldMode ? smoothstep(0.45, 0.72, wn.y) : smoothstep(0.50, 0.85, wn.y);
                float capHeight = (_MorgottCapHeight > 0.0001) ? _MorgottCapHeight : 0.022;
                float tuck = worldMode ? -0.025 : -0.012;
                float maxPuff = capAmt * capHeight * (0.72 + 0.28 * vertNoise);
                float disp = lerp(tuck, maxPuff, upMask);
                float3 puffDir = SafeNormalize(lerp(wn, float3(0.0, 1.0, 0.0), 0.45), float3(0.0, 1.0, 0.0));
                float3 dispDir = SafeNormalize(lerp(wn, puffDir, upMask), wn);
                wp += dispDir * disp;

                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                o.wn = wn;
                o.uv = v.texcoord.xy;
                o.localPos = worldMode ? wp : localPos;
                return o;
            }

            float3 EvalTrilightAmbient(float3 wn)
            {
                float3 sky = unity_AmbientSky.rgb;
                float3 eq  = unity_AmbientEquator.rgb;
                float3 gnd = unity_AmbientGround.rgb;
                return wn.y >= 0.0 ? lerp(eq, sky, saturate(wn.y)) : lerp(eq, gnd, saturate(-wn.y));
            }

            void frag(
                v2f i,
                out half4 gb0 : SV_Target0,
                out half4 gb1 : SV_Target1,
                out half4 gb2 : SV_Target2,
                out half4 em  : SV_Target3)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 wn = SafeNormalize(i.wn, float3(0.0, 1.0, 0.0));
                bool worldMode = _MorgottCapWorldNoise > 0.5;

                float rawUvN = ValueNoise2(i.uv * 18.0) * 0.68 + ValueNoise2(i.uv * 6.0) * 0.32;
                float nUV = saturate((rawUvN - 0.18) / 0.64);
                float rawPosN = NoiseGrad3(i.localPos * 10.0).x * 0.68 + NoiseGrad3(i.localPos * 3.5).x * 0.32;
                float nPos = saturate((rawPosN - 0.22) / 0.56);
                float uvDensity = dot(fwidth(i.uv), float2(1.0, 1.0)) / max(length(fwidth(i.localPos)), 1e-5);
                float n = worldMode ? nPos : lerp(nPos, nUV, smoothstep(0.05, 0.25, uvDensity));

                // Round normal upward into a soft snow cushion + metric powdery bump perturbation.
                float3 baseNormal = SafeNormalize(lerp(wn, float3(0.0, 1.0, 0.0), 0.28), float3(0.0, 1.0, 0.0));
                float3 bp = i.localPos * 9.0;
                float4 ng1 = NoiseGrad3(bp);
                float4 ng2 = NoiseGrad3(bp * 2.13 + 7.3);
                float3 gradLocal = ng1.yzw * 0.65 + ng2.yzw * (0.35 * 2.13);
                float3 gradWS = gradLocal;
                if (!worldMode)
                {
                    float3 ax, ay, az;
                    ObjectAxes(ax, ay, az);
                    gradWS = gradLocal.x * ax + gradLocal.y * ay + gradLocal.z * az;
                }
                float3 gradTan = gradWS - baseNormal * dot(gradWS, baseNormal);
                float3 puffedNormalWS = SafeNormalize(baseNormal - gradTan * 0.08, baseNormal);

                float3 snowAlbedo = float3(0.76, 0.81, 0.88) * (0.94 + 0.06 * n);
                float3 emCol = EvalTrilightAmbient(puffedNormalWS) * snowAlbedo * 0.90;
                #ifndef UNITY_HDR_ON
                    emCol = exp2(-emCol);
                #endif

                gb0 = half4(snowAlbedo, 1.0);
                gb1 = half4(0.04, 0.045, 0.055, 0.24);
                gb2 = half4(puffedNormalWS * 0.5 + 0.5, 1.0);
                em  = half4(emCol, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
