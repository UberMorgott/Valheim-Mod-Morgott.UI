// Spider web silk (Morgott.Creatures spider webs, plan docs/spider-webs-plan.md §9; contract Morgott.Core IVfx.Web /
// WebBurn). Template material "Web" = this shader + the strand atlas (2 x 2 Codex tiles, R = strand coverage).
//   _Mode 0 strands: cards between anchors. UV0 = (tile-local u (repeats), v 0..1, tile id), UV1 = (sheet index into
//           _SheetState, sheet-local x, y m), COLOR.rgb tint, COLOR.a wind sway weight.
//           UV0.w (optional, missing = 1) = card-edge distance 0 rim .. 1 inside: ragged noisy taper toward 0.
//           Tile 4 (TileVanilla) = vanilla Mistlands web art: UV0.xy are direct UVs into _WebTex (the game's
//           fi_village_webs, A = coverage; vertical_web / horizontal_web), bound at runtime by Morgott.UI VfxProvider.
//   _Mode 1 cocoon: helical wraps of the vanilla tunnel web (_WrapTex = tunnelweb_d, A) around object Y, cylindrical
//           coords, no UVs / sway; burns from world _BurnOrigin.
// Burn: one ignition per patch, set once. A fragment ignites at _BurnStart + delay + dist / _SweepSpeed and is gone
// _BurnEdge seconds later (Core WebBurn.GoneAt), clipped at a per-fragment noise threshold < 1 (ragged ember front).
// Premultiplied alpha, double-sided, no depth write, no shadow caster. Everything animated runs on the shader clock.
Shader "Morgott/Web"
{
    Properties
    {
        _Mode ("Mode (0 strands, 1 cocoon)", Float) = 0
        _MainTex ("Strand atlas (R)", 2D) = "black" {}
        _WebTex ("Vanilla web atlas (A), runtime", 2D) = "black" {}
        _WrapTex ("Vanilla tunnel web (A), runtime", 2D) = "black" {}
        _WebGain ("Vanilla coverage gain", Float) = 2.7
        _Color ("Silk colour", Color) = (0.82, 0.84, 0.86, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.9
        _DewColor ("Dew colour", Color) = (1, 1, 1, 1)
        _DewDensity ("Dew cells per tile", Float) = 35
        _DewSparkle ("Dew sparkle", Float) = 1
        _Rime ("Rime / frost", Range(0, 1)) = 0
        _Glow ("Self glow", Float) = 0
        _WindAmp ("Wind sway (m)", Float) = 0.06
        _WindFreq ("Wind frequency (Hz)", Float) = 0.3
        _FadeNear ("Fade start (m)", Float) = 90
        _FadeFar ("Fade end (m)", Float) = 120
        _Tear ("Torn-strand fade", Range(0, 1)) = 0
        _TriScale ("Cocoon tiles per metre", Float) = 0.75
        _BurnStart ("Burn start (shader clock, -1e6 off)", Float) = -1000000
        _Clock ("Clock override (0 = _Time.y)", Float) = 0
        _SweepSpeed ("Sweep speed (m/s)", Float) = 20
        _BurnEdge ("Burn window (s)", Float) = 0.15
        _BurnColor ("Ember colour", Color) = (1, 0.42, 0.08, 1)
        _CharColor ("Char colour", Color) = (0.07, 0.045, 0.03, 1)
        _BurnOrigin ("Cocoon burn origin (world)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            sampler2D _WebTex;
            sampler2D _WrapTex;
            float4 _Color, _DewColor, _BurnColor, _CharColor, _BurnOrigin;
            float _WebGain, _Mode, _Opacity, _DewDensity, _DewSparkle, _Rime, _Glow, _WindAmp, _WindFreq, _FadeNear, _FadeFar, _Tear,
                _TriScale, _BurnStart, _Clock, _SweepSpeed, _BurnEdge;
            float4 _SheetState[32]; // x cut (1 = hidden), y graph delay s, zw burn entry point (sheet-local m)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;
                float3 sheet : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 sheet : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float3 wn : TEXCOORD3;
                float3 op : TEXCOORD4;   // object position in metres (cocoon wraps)
                float4 color : TEXCOORD5; // rgb tint, a distance fade
                UNITY_FOG_COORDS(6)
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Clock() { return _Clock > 0.0 ? _Clock : _Time.y; }

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float sway = _Mode < 0.5 ? v.color.a : 0.0;
                float t = _Time.y * _WindFreq * 6.2831853;
                float ph = dot(wp.xz, float2(0.37, 0.23));
                wp += float3(sin(t + ph) + 0.4 * sin(2.3 * t + ph * 1.7), 0.35 * sin(1.3 * t + ph), 0.6 * cos(0.8 * t + ph * 1.3)) * (_WindAmp * sway);
                o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                o.uv = v.uv;
                o.sheet = v.sheet;
                o.wp = wp;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.op = v.vertex.xyz * length(unity_ObjectToWorld._m00_m10_m20);
                float dist = distance(_WorldSpaceCameraPos, wp);
                float fade = (1.0 - smoothstep(_FadeNear, _FadeFar, dist)) * smoothstep(0.25, 0.9, dist);
                o.color = float4(_Mode < 0.5 ? v.color.rgb : float3(1, 1, 1), fade);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // Atlas tile sample with repeating u; gradients from the unwrapped coords (no mip seam at the wrap), 1 texel inset.
            // Tiles 0-2 have a 6 % black border (wider than any mip footprint). The band tile (3) repeats: BuildVfx squeezes it
            // into 992 texels with 16-texel wrapped gutters, and its LOD is capped at mip 4 (16 texels) to stay inside them.
            float Tile(float2 local, float tile, float2 dx, float2 dy)
            {
                float2 origin = float2(fmod(tile, 2.0), floor(tile * 0.5)) * 0.5;
                float2 f = float2(frac(local.x), saturate(local.y));
                if (tile > 2.5)
                {
                    float2 s = float2(992.0, 1022.0) / 2048.0;
                    dx *= s;
                    dy *= s;
                    float k = min(1.0, 16.0 / (2048.0 * max(length(dx), length(dy)) + 1e-6));
                    return tex2Dgrad(_MainTex, origin + float2(16.0, 1.0) / 2048.0 + f * s, dx * k, dy * k).r;
                }
                return tex2Dgrad(_MainTex, origin + 0.0005 + f * 0.499, dx * 0.499, dy * 0.499).r;
            }

            // Cocoon wrap layer: the vanilla tunnel web (strands run along its u, the dense band is v 0.28..0.70, the
            // range the game's tunnel_web mesh uses) wound k times around object Y; v mirrors so bands stack up the body,
            // a sine tilt makes them cross. a / aB = turns with the seam on opposite sides: equal values, the smaller
            // gradient wins (no mip seam at the atan2 wrap).
            float WrapLayer(float a, float aB, float y, float k, float amp, float ph, float off)
            {
                float2 uA = float2(a * k + off, y + amp * sin(6.2831853 * a + ph));
                float2 uB = float2(aB * k + off, y + amp * sin(6.2831853 * aB + ph));
                float2 dx = ddx(uA), dy = ddy(uA), dxB = ddx(uB), dyB = ddy(uB);
                if (abs(dxB.x) + abs(dyB.x) < abs(dx.x) + abs(dy.x)) { dx = dxB; dy = dyB; }
                float m = abs(frac(uA.y * 0.5) * 2.0 - 1.0);
                return tex2Dgrad(_WrapTex, float2(frac(uA.x), 0.28 + 0.42 * m), dx * float2(1.0, 0.42), dy * float2(1.0, 0.42)).a;
            }

            // Helical ribbon mask: s rises with height and integer steps per turn (seamless), ragged far edge, bundled.
            float Ribbon(float s, float n, float m)
            {
                float b = frac(s);
                float f = 0.12 * (m - 0.5); // fibrous, wandering rims
                float edge = smoothstep(0.0 + f, 0.1 + f, b) * (1.0 - smoothstep(0.4 + 0.14 * n - f, 0.54 + 0.14 * n - f, b));
                return edge * (0.55 + 0.45 * smoothstep(-0.6 + 0.5 * m, 0.7 - 0.3 * m, sin(b * (40.0 + 25.0 * n) + 4.0 * n + 5.0 * m))); // bundles: wavy, uneven thickness
            }

            // Cocoon (user ref cocoon-ref.webp): many tangled, overlapping strands - four vanilla tunnel-web layers at different
            // pitches / wobbles - over a faint silk veil, gathered in places into irregular helical bundles (uneven pitch).
            float Wraps(float3 p)
            {
                float a = atan2(p.z, p.x) / 6.2831853, aB = frac(a + 1.0);
                float n1 = Noise(p * 1.7), n2 = Noise(p * 1.7 + 7.3), m = Noise(p * 6.0);
                float warp = 1.4 * Noise(p * float3(0.9, 0.6, 0.9));
                float r1 = Ribbon(p.y * 3.2 + warp + 3.0 * a + 0.22 * sin(12.566371 * a + 2.0 * n2), n1 * 2.2 - 0.7, m);
                float r2 = Ribbon(p.y * 2.6 - 0.8 * warp - 3.0 * a + 0.37 + 0.18 * sin(18.849556 * a + 1.1), n2 * 2.2 - 0.8, 1.0 - m);
                float l1 = WrapLayer(a, aB, p.y * 1.6, 1.0, 0.25, 0.0, 0.0); // y offsets periodic in a only (mirror period 2: no linear a term)
                float l2 = WrapLayer(a, aB, p.y * 1.3 + 0.5, 1.0, -0.35, 1.3, 0.37);
                float l3 = WrapLayer(a, aB, p.y * 2.2 + 0.2, 2.0, 0.45, 2.4, 0.71);
                float l4 = WrapLayer(a, aB, p.y * 0.9 - 0.3, 1.0, -0.6, 4.1, 0.13);
                float tangle = (max(l1, l2) + 0.6 * max(l3, l4)) * _WebGain * 1.3;
                float r3 = Ribbon(p.y * 2.1 + 1.6 * warp + 0.3 * sin(6.2831853 * a + 3.0 * n1) + 0.61, n1 * 2.0 - 1.0, m); // near-horizontal wraps break the diamond repeat
                float bundles = max(max(r1, r2 * 0.9), r3 * 0.85);
                return saturate(bundles * (0.6 + 0.6 * saturate(tangle)) + (1.0 - bundles) * 0.14 * tangle); // thick crossing bundles, dark gaps with loose strands
            }
            float4 frag(v2f i, float face : VFACE) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 N = normalize(i.wn) * (face > 0 ? 1.0 : -1.0);
                bool cocoon = _Mode > 0.5;

                // ---- coverage --------------------------------------------------------------------------------------
                float strand;
                float dist, delay = 0.0;
                if (cocoon)
                {
                    strand = Wraps(i.op * _TriScale) * lerp(0.35, 1.0, saturate(abs(dot(N, V)) * 2.5)); // no glassy rim at grazing angles
                    dist = distance(i.wp, _BurnOrigin.xyz);
                }
                else
                {
                    strand = i.uv.z > 3.5 ? saturate(tex2D(_WebTex, saturate(i.uv.xy)).a * _WebGain) // tile 4: vanilla art (pow 1.15 below: thin haze, keep strands)
                        : Tile(i.uv.xy, round(i.uv.z), ddx(i.uv.xy), ddy(i.uv.xy));
                    strand = saturate(pow(strand, i.uv.z > 3.5 ? 1.15 : 0.8) * lerp(0.45, 1.35, Noise(i.wp * 0.4)) * lerp(0.55, 1.0, smoothstep(0.04, 0.3, Noise(i.wp * 0.7 + 3.1)))); // gaps thin the fill, long threads stay connected // uneven density + large irregular gaps
                    float4 s = _SheetState[(int)clamp(round(i.sheet.x), 0.0, 31.0)];
                    clip(0.5 - s.x);
                    delay = s.y;
                    dist = distance(i.sheet.yz, s.zw);
                }
                strand *= saturate((i.uv.w * 1.7 - 0.7 * Noise(i.wp * 2.3)) * 3.0); // UV0.w card edge (0 rim .. 1, default 1): ragged taper
                clip(strand - 0.003); // empty atlas texels: no lighting / blending work
                bool burning = _BurnStart > -500000.0;
                float n = burning || _Tear > 0.0 ? Noise(i.wp * 3.1) : 0.5;
                clip(n - _Tear * 1.05); // torn: ragged gaps grow with _Tear

                // ---- burn ------------------------------------------------------------------------------------------
                float cutAt = 0.1 + 0.8 * n;
                float burn = burning ? (Clock() - (_BurnStart + max(delay, 0.0) + dist / max(_SweepSpeed, 0.01))) / max(_BurnEdge, 0.001) : -10.0;
                clip(cutAt - burn);
                float charK = saturate(burn / (cutAt * 0.6)); // wide dark char band, thin glowing edge
                float ember = smoothstep(cutAt - 0.2, cutAt, burn) + 0.35 * saturate(1.0 + burn * 4.0) * step(burn, 0.0); // + pre-heat
                ember *= step(-0.25, burn);

                // ---- light -----------------------------------------------------------------------------------------
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 amb = max(0.0, ShadeSH9(float4(N, 1.0))) + 0.04;
                float3 sun = _LightColor0.rgb * (0.2 + 0.4 * abs(dot(N, L)) + 0.5 * pow(saturate(dot(-V, L)), 6.0)); // + back-lit scatter
                float3 silk = lerp(_Color.rgb, float3(0.86, 0.93, 1.0), _Rime * 0.7) * i.color.rgb;
                float3 col = min(silk * (amb + sun) + silk * _Glow, 0.9); // LDR: stacked layers in noon sun bloomed to white glare (run 20261111-004927)

                // ---- dew: sparse jittered drops on the strands, view-dependent glint -----------------------------------
                if (!cocoon && _DewSparkle > 0.0)
                {
                    float2 g = i.uv.xy * _DewDensity;
                    float3 id = float3(floor(g), i.uv.z);
                    float2 c = 0.2 + 0.6 * float2(Hash(id), Hash(id + 5.3));
                    float drop = (1.0 - smoothstep(0.06, 0.16, length(frac(g) - c))) * step(0.4, Hash(id + 2.1)) * smoothstep(0.25, 0.6, strand);
                    float3 dn = normalize(N + (float3(Hash(id + 1.7), Hash(id + 3.9), Hash(id + 8.1)) - 0.5) * 1.6);
                    float spec = pow(saturate(dot(dn, normalize(L + V))), 48.0);
                    float3 glint = _DewColor.rgb * drop * _DewSparkle * (spec * _LightColor0.rgb * 3.0 + amb + 0.12);
                    col += glint;
                    strand = max(strand, drop * 0.8);
                }

                col = lerp(col, _CharColor.rgb, charK);
                float a = saturate(strand * _Opacity * (1.0 + _Rime * 0.5)) * i.color.a * (1.0 - _Tear * 0.6);
                float4 o = float4(col * a + _BurnColor.rgb * (ember * 1.6 * saturate(strand * 1.5) * i.color.a), a);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, unity_FogColor * o.a);
                return o;
            }
            ENDCG
        }
    }
    Fallback Off
}
