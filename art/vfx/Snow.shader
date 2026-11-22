// Morgott.Core ISnowVfx.Snow(): vanilla-style deep snow outside the Deep North (consumer: Patcher SnowTrails), drawn as an
// extra opaque pass over a vanilla Heightmap chunk's own render mesh (Graphics.DrawMesh). Mirrors the Custom/Heightmap
// Deep North vertex displacement (+Y only; Morgott.Patcher docs\terrain-tess-foot-ik.md "Formula"):
//   wSnow = max(0, 1 - max(|c.r|, |c.g-1|, |c.b|, |c.a|))      (Mountain colour; Seasonality winter paints other biomes so)
//   base  = 0.1 * wSnow * (1 - sat(m.r + m.g + 10 m.b))        (vanilla thin snow, so our surface starts on top of it)
//   deep  = _DepthScale * wSnow * G * (1 - sat(m.r + m.g + 10 m.b)) * smoothstep(_WaterLevel, _WaterLevel + 2.2, y)
// with G = Deep North depth noise baked per chunk by the consumer (_DepthTex over uv0, _DepthGrid texels per side, texel centres on the chunk grid),
// _DepthScale = 2.1 x config scale, m = the chunk's _ClearedMaskTex (dug / cultivated / paved stay bare). Also cut by
// slope (_SlopeMin), distance (_FadeNear.._FadeFar from _Center) and the R8 trail mask _TrailTex over _TrailRect (xy world
// origin, z = 1 / size m; 1 = trampled or under a piece): dy = deep * (1 - trail). Where dy is ~0 the pass is clipped,
// so vanilla terrain shows. ZWrite on + shadow caster: the snow hides what it buries, casts and receives shadows, fogged.
// Lambert, albedo ~0.8. Ambient is added by hand from the linear SH (noambient): the unity/ project is Gamma
// (ProjectSettings m_ActiveColorSpace 0) and Valheim Linear, so Unity's own SH ambient compiled here re-encodes the ambient
// to gamma (ShadeSHPerPixel LinearToGammaSpace, ~2.5x at Valheim's 0.46/0.57/0.71 ambient) and filled the deferred emission
// buffer: shadows washed out, bloom veiled the whole frame (runs 20261123-003403, -031243). Vanilla Custom/Heightmap has no
// such conversion.
Shader "Morgott/Snow"
{
    Properties
    {
        _Color ("Fresh snow", Color) = (0.8, 0.83, 0.88, 1)
        _TrailColor ("Compressed snow", Color) = (0.66, 0.71, 0.8, 1)
        _Drift ("Drift shading", Range(0, 1)) = 0.35
        _SnowNormal ("Snow normal (vanilla Heightmap material)", 2D) = "bump" {}
        _NormalTile ("Snow normal tiles per m", Float) = 0.5
        _TrailTex ("Trail mask", 2D) = "black" {}
        _TrailRect ("Trail rect", Vector) = (0, 0, 0, 0)
        _ClearedMaskTex ("Paint mask", 2D) = "black" {}
        _DepthTex ("Deep snow G", 2D) = "black" {}
        _DepthScale ("Depth scale (m per G)", Float) = 1.05
        _DepthGrid ("Deep snow G texels per side", Float) = 17
        _Center ("Centre", Vector) = (0, 0, 0, 0)
        _FadeNear ("Fade near (m)", Float) = 50
        _FadeFar ("Fade far (m)", Float) = 60
        _WaterLevel ("Water level", Float) = 30
        _SlopeMin ("Slope min (normal.y)", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+1" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow fullforwardshadows noambient nolightmap nodynlightmap nodirlightmap nometa noinstancing
        #pragma target 3.0

        sampler2D _TrailTex, _ClearedMaskTex, _DepthTex, _SnowNormal;
        float4 _TrailRect, _Center;
        fixed4 _Color, _TrailColor;
        half _Drift, _NormalTile, _DepthScale, _DepthGrid, _FadeNear, _FadeFar, _WaterLevel, _SlopeMin;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float dy;
            INTERNAL_DATA
        };

        half Trail(float2 xz)
        {
            float2 uv = (xz - _TrailRect.xy) * _TrailRect.z;
            float2 edge = min(uv, 1 - uv);
            return tex2Dlod(_TrailTex, float4(uv, 0, 0)).r * saturate(min(edge.x, edge.y) * 50); // no hard border at the mask edge (last 1.3 m, past _FadeFar)
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float4 c = v.color;
            float wSnow = max(0, 1 - max(max(abs(c.r), abs(c.g - 1)), max(abs(c.b), abs(c.a))));
            half4 m = tex2Dlod(_ClearedMaskTex, float4(v.texcoord.xy, 0, 0));
            float bare = 1 - saturate(m.r + m.g + 10 * m.b);
            float n = _DepthGrid;
            float g = tex2Dlod(_DepthTex, float4(v.texcoord.xy * ((n - 1) / n) + 0.5 / n, 0, 0)).r;
            float3 wn = UnityObjectToWorldNormal(v.normal + float3(0, 1e-4, 0)); // never normalize a zero normal (NaN)
            float deep = _DepthScale * wSnow * g * bare * smoothstep(_WaterLevel, _WaterLevel + 2.2, wp.y);
            deep *= smoothstep(_SlopeMin, _SlopeMin + 0.15, wn.y) * (1 - smoothstep(_FadeNear, _FadeFar, distance(wp.xz, _Center.xz)));
            float dy = deep * (1 - Trail(wp.xz));
            // Heightmap chunks are translation only: object y = world y.
            v.vertex.y += 0.1 * wSnow * bare + dy;
            // Tilt the normal by the trail's slope (0.5 m central difference) so trail walls catch the light.
            float2 grad = float2(Trail(wp.xz + float2(0.5, 0)) - Trail(wp.xz - float2(0.5, 0)),
                                 Trail(wp.xz + float2(0, 0.5)) - Trail(wp.xz - float2(0, 0.5))) * deep; // d(-dy) per m
            float ny = max(wn.y, 0.05);
            v.normal = normalize(mul((float3x3)unity_WorldToObject, float3(wn.x + grad.x * ny, ny, wn.z + grad.y * ny)));
            o.dy = dy;
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
            clip(IN.dy - 0.02); // no deep snow here: vanilla terrain (and its thin snow) shows through
            float2 xz = IN.worldPos.xz;
            half t = Trail(xz);
            half drift = lerp(1, 0.9 + 0.2 * (Noise(xz * 0.35) * 0.65 + Noise(xz * 1.7) * 0.35), _Drift);
            o.Albedo = saturate(lerp(_Color.rgb * drift, _TrailColor.rgb, t));
            o.Normal = UnpackNormal(tex2D(_SnowNormal, xz * _NormalTile));
            half4 n = half4(normalize(WorldNormalVector(IN, o.Normal)), 1); // L2 assumes a unit normal
            o.Emission = o.Albedo * max(0, SHEvalLinearL0L1(n) + SHEvalLinearL2(n)); // linear ambient whatever the build's colour space
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack Off
}
