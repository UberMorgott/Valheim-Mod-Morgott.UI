// Fullscreen Deferred seasonal pass (consumer: Patcher): paints winter snow on world geometry (bushes/pines/rocks/
// ruins/grown crops/roofs) and autumn fallen-leaf carpet under deciduous trees while skipping skybox (stencil 0x80 == 1)
// and characters marked by CreatureSnow Pass 1 (stencil 0x04 == 0). Preserves GB0.a (AO), GB1.a (smoothness), GB2, GB3.
Shader "Morgott/WorldSeason"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WorldSeasonDeferred"
            ZTest Always
            ZWrite Off
            Cull Off
            Stencil
            {
                Ref 128
                ReadMask 132
                Comp Equal
            }
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

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            sampler2D _CameraGBufferTexture2;
            sampler2D _MorgottShelterTex;

            float _MorgottWorldSnowActive;
            float _MorgottSeasonSnow;
            float _MorgottSeasonLeafLitter;
            float _MorgottWaterLevel;
            float4 _MorgottShelterRect;

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            v2f vert(uint vid : SV_VertexID)
            {
                v2f o;
                float2 p = float2((vid == 1) ? 3.0 : -1.0, (vid == 2) ? 3.0 : -1.0);
                o.pos = float4(p, 0.0, 1.0);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            float3 SafeNormalize(float3 v, float3 fallback)
            {
                float d = dot(v, v);
                return d > 1e-12 ? v * rsqrt(d) : fallback;
            }

            float4 Hash24(float2 p)
            {
                float4 p4 = frac(float4(p.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
                p4 += dot(p4, p4.wzxy + 33.33);
                return frac((p4.xxyz + p4.yzzw) * p4.zywx);
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

            float3 LeafPalette(float t, float shade)
            {
                float3 gold    = float3(0.58, 0.27, 0.03);
                float3 orange  = float3(0.48, 0.11, 0.02);
                float3 crimson = float3(0.28, 0.025, 0.015);
                float3 russet  = float3(0.17, 0.055, 0.018);
                float3 c = (t < 0.28) ? gold : (t < 0.56) ? orange : (t < 0.80) ? crimson : russet;
                return c * (0.86 + 0.26 * shade);
            }

            void LeafLayer(float2 xz, float scale, float seed, inout float3 col, inout float mask)
            {
                float2 p = xz * scale;
                float2 base = floor(p - 0.5);
                float2 rel = p - base;

                [unroll]
                for (int oy = 0; oy <= 1; oy++)
                {
                    [unroll]
                    for (int ox = 0; ox <= 1; ox++)
                    {
                        float2 cell = base + float2(ox, oy);
                        float4 h = Hash24(cell + seed);
                        if (h.w < 0.14) continue;

                        float2 center = float2(ox + 0.5, oy + 0.5) + (h.xy - 0.5) * 0.48;
                        float2 d = rel - center;
                        float sn, cs;
                        sincos(h.z * 6.2831853, sn, cs);
                        float2 ld = float2(cs * d.x + sn * d.y, -sn * d.x + cs * d.y);

                        float leafLen = 0.34 + 0.11 * frac(h.w * 7.13);
                        float leafWid = 0.16 + 0.06 * frac(h.z * 5.71);
                        float2 q = ld / float2(leafLen, leafWid);
                        q.y /= max(0.16, 1.0 - 0.65 * q.x * q.x);

                        float shape = 1.0 - smoothstep(0.82, 1.0, length(q));
                        if (shape <= 0.002) continue;

                        float3 leafCol = LeafPalette(frac(h.x * 13.7 + h.y * 5.3), frac(h.w * 11.3));
                        float newMask = mask + shape * (1.0 - mask);
                        col = (col * mask * (1.0 - shape) + leafCol * shape) / max(newMask, 1e-4);
                        mask = newMask;
                    }
                }
            }

            void frag(
                v2f i,
                out half4 gb0 : SV_Target0,
                out half4 gb1 : SV_Target1)
            {
                if (_MorgottWorldSnowActive < 0.5) discard;
                if (_MorgottSeasonLeafLitter <= 0.02 && _MorgottSeasonSnow <= 0.08) discard;

                float2 uv = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                #if defined(UNITY_REVERSED_Z)
                    float zGL = 1.0 - 2.0 * rawDepth;
                #else
                    float zGL = 2.0 * rawDepth - 1.0;
                #endif
                float4 clipPos = float4(uv * 2.0 - 1.0, zGL, 1.0);
                float4 viewPos = mul(unity_CameraInvProjection, clipPos);
                viewPos.xyz /= viewPos.w;
                // unity_CameraInvProjection outputs RH view space (-Z forward); unity_CameraToWorld expects LH (+Z forward).
                float3 worldPos = mul(unity_CameraToWorld, float4(viewPos.x, viewPos.y, -viewPos.z, 1.0)).xyz;

                if (worldPos.y > 3000.0 || worldPos.y < _MorgottWaterLevel + 0.06) discard;

                float camDist = distance(worldPos, _WorldSpaceCameraPos);
                float distFade = 1.0 - smoothstep(115.0, 155.0, camDist);
                if (distFade <= 0.005) discard;

                float3 wn = SafeNormalize(tex2Dlod(_CameraGBufferTexture2, float4(uv, 0.0, 0.0)).xyz * 2.0 - 1.0, float3(0.0, 1.0, 0.0));

                float2 suv = (worldPos.xz - _MorgottShelterRect.xy) * _MorgottShelterRect.z;
                float3 shSample = tex2Dlod(_MorgottShelterTex, float4(saturate(suv), 0.0, 0.0)).rgb;
                float3 sh = (suv.x >= 0.0 && suv.x <= 1.0 && suv.y >= 0.0 && suv.y <= 1.0) ? shSample : float3(0.0, -1000.0, 0.0);
                float roofWorldY = _MorgottShelterRect.w + sh.g;
                float underRoofOrFire = (sh.r > 0.20 && worldPos.y < roofWorldY - 0.20) ? smoothstep(0.20, 0.72, sh.r) : 0.0;

                float3 leafColor = float3(0.35, 0.14, 0.02);
                float leafAlpha = 0.0;
                if (_MorgottSeasonLeafLitter > 0.02 && sh.b > 0.04)
                {
                    float leafMask = 0.0;
                    LeafLayer(worldPos.xz, 4.8, 17.3, leafColor, leafMask);
                    LeafLayer(worldPos.xz, 7.5, 53.9, leafColor, leafMask);
                    leafAlpha = leafMask * sh.b * _MorgottSeasonLeafLitter * (1.0 - underRoofOrFire) * smoothstep(0.70, 0.88, wn.y) * distFade * 0.88;
                }

                float3 snowColor = float3(0.75, 0.80, 0.87);
                float snowAlpha = 0.0;
                if (_MorgottSeasonSnow > 0.08)
                {
                    float rawN = ValueNoise3(worldPos * 2.6) * 0.65 + ValueNoise3(worldPos * 9.0) * 0.35;
                    float n = saturate((rawN - 0.22) / 0.56);
                    float snowAmt = smoothstep(0.08, 0.95, _MorgottSeasonSnow);
                    float minUp = lerp(0.82, 0.48, pow(snowAmt, 0.7));
                    float snowMask = smoothstep(minUp - 0.14, minUp + 0.14, wn.y + (n - 0.5) * 0.22);
                    snowAlpha = snowMask * snowAmt * (1.0 - underRoofOrFire) * distFade * 0.86;
                    snowColor *= (0.94 + 0.06 * n);
                }

                float outAlpha = snowAlpha + leafAlpha * (1.0 - snowAlpha);
                if (outAlpha < 0.008) discard;

                float3 outAlbedo = lerp(leafColor, snowColor, snowAlpha / max(outAlpha, 1e-4));
                gb0 = half4(outAlbedo, outAlpha);
                gb1 = half4(0.035, 0.04, 0.048, outAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
