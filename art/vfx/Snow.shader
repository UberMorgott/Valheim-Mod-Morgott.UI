// Morgott.Core ISnowVfx.Snow(): thin fresh snow with trampled trails, an extra alpha-blended pass over a vanilla
// Heightmap chunk's own render mesh (consumer: Patcher SnowTrails, Graphics.DrawMesh). Lit like terrain (Lambert on the
// chunk's normals, so the ground shape stays readable), Unity fog, receives shadows. Coverage = vertex colour G (Heightmap
// Mountain / DeepNorth colour; Seasonality winter paints other biomes so) x not dirt / cultivated / paved in the chunk's
// _ClearedMaskTex (DeepSnow paint 1,1,1,1 stays snow) x above _WaterLevel x flatter than _SlopeMin, fading out between
// _FadeNear and _FadeFar (m, xz) from _Center. _TrailTex = R8 trail mask (1 = trampled) over _TrailRect (xy world origin,
// z = 1 / size m, w = texel in uv): trails take _TrailColor plus a little sun-side relief from the mask gradient.
Shader "Morgott/Snow"
{
    Properties
    {
        _Color ("Fresh snow", Color) = (0.95, 0.97, 1, 1)
        _TrailColor ("Compressed snow", Color) = (0.68, 0.75, 0.87, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.8
        _Drift ("Drift shading", Range(0, 1)) = 0.35
        _TrailTex ("Trail mask", 2D) = "black" {}
        _TrailRect ("Trail rect", Vector) = (0, 0, 0, 0)
        _ClearedMaskTex ("Paint mask", 2D) = "black" {}
        _Center ("Centre", Vector) = (0, 0, 0, 0)
        _FadeNear ("Fade near (m)", Float) = 22
        _FadeFar ("Fade far (m)", Float) = 30
        _WaterLevel ("Water level", Float) = 30
        _SlopeMin ("Slope min (normal.y)", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+1" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        ZWrite Off
        ZTest LEqual
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Lambert decal:blend vertex:vert nolightmap nodynlightmap nodirlightmap nometa noinstancing
        #pragma target 3.0

        sampler2D _TrailTex, _ClearedMaskTex;
        float4 _TrailRect, _Center;
        fixed4 _Color, _TrailColor;
        half _Opacity, _Drift, _FadeNear, _FadeFar, _WaterLevel, _SlopeMin;

        struct Input
        {
            float4 color : COLOR;
            float2 maskUv;
            float3 wn;
            float3 worldPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.maskUv = v.texcoord.xy;
            o.wn = UnityObjectToWorldNormal(v.normal);
        }

        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 p = IN.worldPos;
            half4 paint = tex2D(_ClearedMaskTex, IN.maskUv);
            half cleared = max(paint.r, max(paint.g, paint.b)) - min(paint.r, min(paint.g, paint.b));
            half cov = IN.color.g * (1 - saturate(cleared * 2));
            cov *= saturate((p.y - _WaterLevel) * 2);
            cov *= smoothstep(_SlopeMin, _SlopeMin + 0.15, normalize(IN.wn).y);
            cov *= 1 - smoothstep(_FadeNear, _FadeFar, distance(p.xz, _Center.xz));
            clip(cov - 0.004);

            float2 uv = (p.xz - _TrailRect.xy) * _TrailRect.z;
            float2 edge = min(uv, 1 - uv);
            half inMask = saturate(min(edge.x, edge.y) * 10); // trails fade out over the last tenth, never a hard border
            float e = _TrailRect.w;
            half t = tex2Dlod(_TrailTex, float4(uv, 0, 0)).r * inMask;
            float2 g = float2(tex2Dlod(_TrailTex, float4(uv + float2(e, 0), 0, 0)).r - tex2Dlod(_TrailTex, float4(uv - float2(e, 0), 0, 0)).r,
                              tex2Dlod(_TrailTex, float4(uv + float2(0, e), 0, 0)).r - tex2Dlod(_TrailTex, float4(uv - float2(0, e), 0, 0)).r) * inMask;

            half drift = lerp(1, 0.9 + 0.2 * (Noise(p.xz * 0.35) * 0.65 + Noise(p.xz * 1.7) * 0.35), _Drift);
            fixed3 albedo = lerp(_Color.rgb * drift, _TrailColor.rgb, t);
            albedo *= 1 + dot(g, normalize(_WorldSpaceLightPos0.xz + 1e-4)) * 0.6; // print wall facing the sun brighter, far wall darker
            o.Albedo = albedo;
            o.Alpha = cov * lerp(_Opacity, 1, t);
        }
        ENDCG
    }
    FallBack Off
}
