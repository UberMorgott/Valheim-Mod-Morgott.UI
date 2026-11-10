// Spider web silk (Morgott.Creatures spider webs, plan docs/spider-webs-plan.md §9; contract Morgott.Core IVfx.Web /
// WebBurn). Template material "Web" = this shader + the strand atlas (2 x 2 Codex tiles, R = strand coverage).
//   _Mode 0 strands: cards between anchors. UV0 = (tile-local u (repeats), v 0..1, tile id), UV1 = (sheet index into
//           _SheetState, sheet-local x, y m), COLOR.rgb tint, COLOR.a wind sway weight.
//   _Mode 1 cocoon: object-space triplanar wraps of the band tile, no UVs / sway; burns from world _BurnOrigin.
// Burn: one ignition per patch, set once. A fragment ignites at _BurnStart + delay + dist / _SweepSpeed and is gone
// _BurnEdge seconds later (Core WebBurn.GoneAt), clipped at a per-fragment noise threshold < 1 (ragged ember front).
// Premultiplied alpha, double-sided, no depth write, no shadow caster. Everything animated runs on the shader clock.
Shader "Morgott/Web"
{
    Properties
    {
        _Mode ("Mode (0 strands, 1 cocoon)", Float) = 0
        _MainTex ("Strand atlas (R)", 2D) = "black" {}
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
            float4 _Color, _DewColor, _BurnColor, _CharColor, _BurnOrigin;
            float _Mode, _Opacity, _DewDensity, _DewSparkle, _Rime, _Glow, _WindAmp, _WindFreq, _FadeNear, _FadeFar, _Tear,
                _TriScale, _BurnStart, _Clock, _SweepSpeed, _BurnEdge;
            float4 _SheetState[32]; // x cut (1 = hidden), y graph delay s, zw burn entry point (sheet-local m)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float3 uv : TEXCOORD0;
                float3 sheet : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uv : TEXCOORD0;
                float3 sheet : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float3 wn : TEXCOORD3;
                float3 op : TEXCOORD4;   // object position in metres (cocoon triplanar)
                float3 on : TEXCOORD5;   // object normal
                float4 color : TEXCOORD6; // rgb tint, a distance fade
                UNITY_FOG_COORDS(7)
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
                o.on = v.normal;
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

            // Cocoon: two crossing tilted strand layers under broad ribbons with ragged, fibrous edges (bandage wraps).
            float Wraps(float2 p)
            {
                float2 a = float2(p.x, p.y + 0.35 * p.x + 0.06 * sin(p.x * 5.1));
                float2 b = float2(p.x + 0.37, p.y - 0.55 * p.x + 0.29);
                float2 c = float2(p.x * 3.0 + 0.11, p.y * 3.0 + 0.9 * p.x);
                float l1 = Tile(float2(a.x, frac(a.y)), 3.0, ddx(a), ddy(a));
                float l2 = Tile(float2(b.x, frac(b.y)), 3.0, ddx(b), ddy(b));
                float fibre = Tile(float2(c.x, frac(c.y)), 3.0, ddx(c), ddy(c));
                float wave = sin((p.y + 0.3 * p.x) * 24.0 + 2.5 * sin(p.x * 3.1 + p.y * 1.7));
                float ribbon = smoothstep(0.45, 0.8, 0.5 + 0.5 * wave + 0.35 * (Noise(float3(p * 7.0, 0.0)) - 0.5) + 0.15 * (fibre - 0.3));
                return saturate(max(l1, l2 * 0.8) * lerp(0.35, 3.0, ribbon) + ribbon * (0.45 + 0.5 * fibre));
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
                    float3 p = i.op * _TriScale;
                    float3 w = pow(abs(normalize(i.on)), 4.0);
                    w /= w.x + w.y + w.z;
                    strand = Wraps(p.zy) * w.x + Wraps(p.xz) * w.y + Wraps(p.xy) * w.z;
                    dist = distance(i.wp, _BurnOrigin.xyz);
                }
                else
                {
                    strand = Tile(i.uv.xy, round(i.uv.z), ddx(i.uv.xy), ddy(i.uv.xy));
                    strand = saturate(pow(strand, 0.8) * lerp(0.65, 1.35, Noise(i.wp * 0.4))); // uneven density across a patch
                    float4 s = _SheetState[(int)clamp(round(i.sheet.x), 0.0, 31.0)];
                    clip(0.5 - s.x);
                    delay = s.y;
                    dist = distance(i.sheet.yz, s.zw);
                }
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
                float3 sun = _LightColor0.rgb * (0.3 + 0.5 * abs(dot(N, L)) + 1.6 * pow(saturate(dot(-V, L)), 6.0)); // + back-lit scatter
                float3 silk = lerp(_Color.rgb, float3(0.86, 0.93, 1.0), _Rime * 0.7) * i.color.rgb;
                float3 col = silk * (amb + sun) + silk * _Glow;

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
