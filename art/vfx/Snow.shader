// Morgott.Core ISnowVfx.Snow(): vanilla-style deep snow outside the Deep North (consumer: Patcher SnowTrails), drawn as an
// extra opaque pass over a vanilla Heightmap chunk's own render mesh (Graphics.DrawMesh). Mirrors the Custom/Heightmap
// Deep North vertex displacement (+Y only; Morgott.Patcher docs\terrain-tess-foot-ik.md "Formula"):
//   wSnow = max(0, 1 - max(|c.r|, |c.g-1|, |c.b|, |c.a|))      (Mountain colour; Seasonality winter paints other biomes so)
//   base  = 0.1 * wSnow * (1 - sat(m.r + m.g + 10 m.b))        (vanilla thin snow, so our surface starts on top of it)
//   deep  = _DepthScale * wSnow * G * (1 - sat(m.r + m.g + 10 m.b)) * smoothstep(_WaterLevel, _WaterLevel + 2.2, y)
// with G = Deep North depth noise baked per chunk by the consumer (_DepthTex over uv0, _DepthGrid texels per side, texel centres on the chunk grid),
// _DepthScale = 2.1 x config scale, m = the chunk's _ClearedMaskTex (dug / cultivated / paved stay bare). Also cut by
// slope (_SlopeMin), distance (_FadeNear.._FadeFar from _Center) and the R8 trail mask _TrailTex over _TrailRect (xy world
// origin, z = 1 / size m; prints write up to 0.9, pieces 1): dy = max(deep * (1 - trail), min(deep, 0.06) * (1 - piece)),
// piece = smoothstep(0.92, 0.98, trail). Trails keep a 0.06 m floor of compressed snow on top of the 0.1 m base, as vanilla
// Deep North trails bottom out on its 0.1 m thin snow (m.g -> 0 brings the base term back, terrain-tess-foot-ik.md "Formula"),
// instead of clipping the pass and exposing the (tessellated, angular) vanilla terrain. Where dy is ~0 (no deep snow, or
// under a piece) the pass is clipped, so vanilla terrain shows. Trampled colour + trench-wall shading are per pixel from the
// bilinear mask; the mesh is tessellated like vanilla's (x4 within 10 m), so the trench walls follow it; the wall slope is the mask's world gradient from the pixel
// derivatives of that one sample (no extra fetches; faded where the pixel footprint is near-degenerate). ZWrite on + shadow
// caster: the snow hides what it buries, casts and receives shadows, fogged. The consumer draws it one queue before the
// terrain, so the terrain it covers is depth-rejected (queue Geometry+1 here is only the default). Shadow caster: no
// normal tilt / SH work.
// Lambert, albedo ~0.8. Ambient is added by hand from the linear SH (noambient): the unity/ project is Gamma
// (ProjectSettings m_ActiveColorSpace 0) and Valheim Linear, so Unity's own SH ambient compiled here re-encodes the ambient
// to gamma (ShadeSHPerPixel LinearToGammaSpace, ~2.5x at Valheim's 0.46/0.57/0.71 ambient) and filled the deferred emission
// buffer: shadows washed out, bloom veiled the whole frame (runs 20261123-003403, -031243). Vanilla Custom/Heightmap has no
// such conversion. SH split as Unity's own ShadeSHPerVertex / ShadeSHPerPixel (UnityStandardUtils.cginc): L2 per vertex,
// L0L1 per pixel with the normal-mapped normal.
Shader "Morgott/Snow"
{
    Properties
    {
        _Color ("Fresh snow", Color) = (0.8, 0.83, 0.88, 1)
        _TrailColor ("Compressed snow", Color) = (0.6, 0.66, 0.78, 1)
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
        _Tess ("Tessellation (vanilla Heightmap _Tess)", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+1" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert tessellate:tess addshadow fullforwardshadows noambient nolightmap nodynlightmap nodirlightmap nometa noinstancing
        #pragma target 4.6
        #include "Tessellation.cginc"

        sampler2D _TrailTex, _ClearedMaskTex, _DepthTex, _SnowNormal;
        float4 _TrailRect, _Center;
        fixed4 _Color, _TrailColor;
        half _Drift, _NormalTile, _DepthScale, _DepthGrid, _FadeNear, _FadeFar, _WaterLevel, _SlopeMin, _Tess;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 color : COLOR; // x dy, yzw SH L2 (vert repacks the vertex colour: tessellated surface shaders take no custom Input)
            float2 uv2_TrailTex;  // tl: metres from the trail mask origin, small numbers, exact pixel derivatives (world xz is ~1 mm grainy far out)
            INTERNAL_DATA
        };

        half Trail(float2 tl)
        {
            float2 uv = tl * _TrailRect.z;
            float2 edge = min(uv, 1 - uv);
            return tex2Dlod(_TrailTex, float4(uv, 0, 0)).r * saturate(min(edge.x, edge.y) * 50); // no hard border at the mask edge (last 1.3 m, past _FadeFar)
        }

        // Vanilla Heightmap hull factor (terrain-tess-foot-ik.md: _Tess * clamp(1 - (dist - 10) * 0.025, 0.01, 1)): x4 within 10 m,
        // ~1 by 50 m. Without it the trench was displaced only at the 1 m chunk vertices: angular ledges along its walls and a
        // straight seam where the per-pixel trail colour / wall shade left the coarse geometry (run 20261124-014538).
        float4 tess(appdata_full v0, appdata_full v1, appdata_full v2)
        {
            return UnityDistanceBasedTess(v0.vertex, v1.vertex, v2.vertex, 10, 50, _Tess);
        }

        void vert(inout appdata_full v)
        {
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
            float2 tl = wp.xz - _TrailRect.xy;
            half t = Trail(tl);
            float dy = max(deep * (1 - t), min(deep, 0.06) * (1 - smoothstep(0.92, 0.98, t))); // trail floor; pieces (1) cut it all
            // Heightmap chunks are translation only: object y = world y.
            v.vertex.y += 0.1 * wSnow * bare + dy;
            v.color = float4(dy, 0, 0, 0);
            v.texcoord1 = float4(tl, 0, 0); // read back as uv2_TrailTex (_TrailTex_ST stays identity)
#if !defined(UNITY_PASS_SHADOWCASTER) // the caster only needs the height (and the clip)
            // Tilt the normal by the trail's slope (0.5 m central difference) so trail walls catch the light.
            float2 grad = float2(Trail(tl + float2(0.5, 0)) - Trail(tl - float2(0.5, 0)),
                                 Trail(tl + float2(0, 0.5)) - Trail(tl - float2(0, 0.5))) * deep; // d(-dy) per m
            float ny = max(wn.y, 0.05);
            float3 nw = normalize(float3(wn.x + grad.x * ny, ny, wn.z + grad.y * ny));
            v.normal = mul((float3x3)unity_WorldToObject, nw);
            v.color.yzw = SHEvalLinearL2(half4(nw, 1));
#endif
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
            clip(IN.color.x - 0.02); // no deep snow here: vanilla terrain (and its thin snow) shows through
            float2 xz = IN.worldPos.xz;
            float2 tl = IN.uv2_TrailTex;
            float t = Trail(tl);
            // |world gradient of t| from screen derivatives: [ddx(tl); ddy(tl)] g = (ddx t, ddy t), solved by Cramer
            float2 px = ddx(tl), py = ddy(tl), dt = float2(ddx(t), ddy(t));
            float det = px.x * py.y - px.y * py.x;
            float2 num = float2(dt.x * py.y - px.y * dt.y, px.x * dt.y - py.x * dt.x);
            half grazing = saturate(10 * abs(det) / max(length(px) * length(py), 1e-12)); // |sin| of the footprint angle
            half wall = saturate(length(num) / max(abs(det), 1e-12)) * grazing; // mask slope per m (was the 0.25 m difference x 4)
            half drift = lerp(1, 0.9 + 0.2 * (Noise(xz * 0.35) * 0.65 + Noise(xz * 1.7) * 0.35), _Drift);
            o.Albedo = saturate(lerp(_Color.rgb * drift, _TrailColor.rgb, saturate(t / 0.9))) * (1 - 0.18 * wall); // trench walls in shade
            o.Normal = UnpackNormal(tex2D(_SnowNormal, xz * _NormalTile));
            half4 n = half4(normalize(WorldNormalVector(IN, o.Normal)), 1); // L2 assumes a unit normal
            o.Emission = o.Albedo * max(0, SHEvalLinearL0L1(n) + IN.color.yzw); // linear ambient whatever the build's colour space
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack Off
}
