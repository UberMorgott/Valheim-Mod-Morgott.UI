// Vaettir creature shell (Monsters/InfusionLooks.Shell.cs, design docs/rarity-shell-design.md). Idea after Path of
// Valheiman's rarity shell; written from scratch. One extra material slot on the creature's own renderers (no second
// skinning): three passes over the same skinned vertices.
//   Hull  (Cull Front, additive): the rarity line, an inverted hull pushed out along the normal by _Line metres.
//   Halo  (Cull Front, additive): a wider, softer hull (_HaloWidth x the line); degenerate when _Halo == 0 (magic).
//   Body  (Cull Back, premultiplied alpha): the tier's Fresnel rim + the infusion surface (_Surface pattern id).
// Per renderer (MaterialPropertyBlock on the shell slot): _Size (creature radius factor). Everything animated runs on
// _Time, nothing per frame on the CPU. Distance: _FadeNear.._FadeFar fades the whole shell out (rgb, the blend is
// additive / premultiplied); the line widens with view distance (x1 at 8 m, x2.2 at 40 m), capped at 0.25 m, and never
// gets thinner than _MinPixels on screen (halo: x _HaloWidth): a sub-pixel hull drops in and out of rasterisation from
// frame to frame as the creature animates (flicker bug 2026-11-07: magic 0.012 m < 1 px past ~12 m at 1080p).
// Fog: additive passes fade toward black, the body toward the fog colour (UNITY_APPLY_FOG).
Shader "Morgott/Shell"
{
    Properties
    {
        _LineColor ("Line colour", Color) = (0.3, 0.55, 1, 1)
        _Line ("Line width (m)", Float) = 0.012
        _LineGlow ("Line glow", Float) = 1.2
        _Halo ("Halo strength", Float) = 0
        _HaloWidth ("Halo width (x line)", Float) = 3
        _Pulse ("Pulse depth", Range(0, 1)) = 0
        _PulseSpeed ("Pulse speed", Float) = 2
        _Band ("Travelling band", Range(0, 1)) = 0
        _RimGlow ("Tier rim on the body", Float) = 0.15
        _RimPower ("Rim power", Float) = 3
        _Surface ("Surface pattern (0 none)", Float) = 0
        _SurfaceColor ("Surface colour", Color) = (1, 1, 1, 1)
        _SurfaceGlow ("Surface glow", Float) = 1
        _Size ("Creature size factor", Float) = 1
        _FadeNear ("Fade start (m)", Float) = 30
        _FadeFar ("Fade end (m)", Float) = 45
        _MinPixels ("Min line width (px)", Float) = 1.5
        _Flash ("Flash (shield hit)", Range(0, 1)) = 1
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    float4 _LineColor;
    float _Line, _LineGlow, _Halo, _HaloWidth, _Pulse, _PulseSpeed, _Band, _RimGlow, _RimPower;
    float _Surface;
    float4 _SurfaceColor;
    float _SurfaceGlow, _Size, _FadeNear, _FadeFar, _Flash, _MinPixels;

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 wn : TEXCOORD0;   // world normal
        float3 view : TEXCOORD1; // world dir to the camera
        float3 op : TEXCOORD2;   // object position in metres (object orientation)
        float4 misc : TEXCOORD3; // x fade, y creature-relative height (m), z near-camera fade
        UNITY_FOG_COORDS(4)
    };

    float Fade(float dist)
    {
        return 1.0 - smoothstep(_FadeNear, _FadeFar, dist);
    }

    // Hull vertex: pushed out along the world normal by width metres (0 = body), at least minPx screen pixels.
    v2f Shell(appdata v, float width, float minPx)
    {
        v2f o;
        float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
        float3 wn = normalize(UnityObjectToWorldNormal(v.normal));
        float dist = distance(_WorldSpaceCameraPos, wp);
        float grow = lerp(1.0, 2.2, saturate((dist - 8.0) / 32.0));
        float pixel = 2.0 * dist / (abs(UNITY_MATRIX_P._m11) * _ScreenParams.y); // metres per screen pixel at this distance
        wp += wn * min(0.25, max(width * grow * _Size, minPx * pixel));
        o.pos = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
        o.wn = wn;
        o.view = _WorldSpaceCameraPos - wp;
        float scale = length(unity_ObjectToWorld._m00_m10_m20);
        o.op = v.vertex.xyz * scale;
        o.misc = float4(Fade(dist), mul((float3x3)unity_ObjectToWorld, v.vertex.xyz).y, smoothstep(0.6, 1.6, dist), 0);
        UNITY_TRANSFER_FOG(o, o.pos);
        return o;
    }

    float Pulse()
    {
        return 1.0 - _Pulse + _Pulse * (0.5 + 0.5 * sin(_Time.y * _PulseSpeed));
    }

    // Unique: a bright band travelling up the creature (creature-relative height, so terrain never shifts its phase).
    float BandAt(float h)
    {
        float b = frac(h * 0.55 - _Time.y * 0.45);
        return 1.0 + _Band * 2.2 * smoothstep(0.82, 0.97, b) * (1.0 - smoothstep(0.97, 1.0, b));
    }

    // ---- procedural noise (object space metres, no textures) --------------------------------------------------------
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

    float Fbm(float3 x)
    {
        return 0.55 * Noise(x) + 0.3 * Noise(x * 2.03) + 0.15 * Noise(x * 4.01);
    }

    // Distance to the nearest cell edge (0 on an edge), and the cell's random id.
    float2 Cells(float3 x)
    {
        float3 i = floor(x);
        float3 f = frac(x);
        float d1 = 8.0, d2 = 8.0, id = 0.0;
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int k = -1; k <= 1; k++)
        {
            float3 g = float3(k, y, z);
            float3 c = g + float3(Hash(i + g), Hash(i + g + 7.1), Hash(i + g + 13.3)) - f;
            float d = dot(c, c);
            if (d < d1) { d2 = d1; d1 = d; id = Hash(i + g + 3.7); }
            else if (d < d2) d2 = d;
        }
        return float2(sqrt(d2) - sqrt(d1), id);
    }

    // Surface pattern: premultiplied rgb (glow added on top) + alpha (tint over the body).
    float4 Surface(float id, float3 p, float fres, float t)
    {
        float3 c = _SurfaceColor.rgb;
        if (id < 0.5) return 0;
        if (id < 1.5) // hide_pierce: cold steel sheen bands sweeping down
        {
            float band = frac(p.y * 1.6 + p.x * 0.4 + t * 0.6);
            float sheen = smoothstep(0.8, 0.95, band) * (1.0 - smoothstep(0.95, 1.0, band));
            float a = 0.16 + 0.2 * fres;
            return float4(c * (a + sheen * 0.9 * _SurfaceGlow), a);
        }
        if (id < 2.5) // hide_slash: bronze scale cells, dark seams
        {
            float2 cell = Cells(p * 9.0);
            float seam = 1.0 - smoothstep(0.0, 0.09, cell.x);
            float a = 0.18 + 0.32 * seam;
            float3 col = lerp(c * (0.7 + 0.5 * cell.y), c * 0.18, seam);
            return float4(col * a + c * fres * 0.25 * _SurfaceGlow, a);
        }
        if (id < 3.5) // hide_blunt: stone, dark crack veins
        {
            float n = Fbm(p * 5.0);
            float vein = 1.0 - smoothstep(0.0, 0.06, abs(n - 0.5));
            float a = 0.24 + 0.45 * vein;
            float3 col = lerp(c * (0.8 + 0.4 * Noise(p * 14.0)), float3(0.04, 0.035, 0.03), vein);
            return float4(col * a, a);
        }
        if (id < 4.5) // ward: violet hex shimmer, Fresnel weighted, flicker
        {
            float2 cell = Cells(p * 6.0 + float3(0, t * 0.2, 0));
            float edge = 1.0 - smoothstep(0.0, 0.05, cell.x);
            float flick = 0.75 + 0.25 * sin(t * 9.0 + cell.y * 40.0);
            float g = (edge * 0.8 + 0.25) * (0.35 + 0.9 * fres) * flick;
            return float4(c * g * _SurfaceGlow, 0.08 * fres);
        }
        if (id < 5.5) // fire: ember flicker rising
        {
            float n = Fbm(p * 4.0 - float3(0, t * 1.8, 0));
            float g = smoothstep(0.45, 0.8, n) * (0.4 + fres) ;
            return float4(lerp(c, float3(1, 0.85, 0.3), smoothstep(0.7, 0.9, n)) * g * _SurfaceGlow, 0.06 * g);
        }
        if (id < 6.5) // frost: icy facets, pale rim
        {
            float2 cell = Cells(p * 7.0);
            float edge = 1.0 - smoothstep(0.0, 0.06, cell.x);
            float a = 0.14 + 0.18 * fres;
            return float4(c * a + c * (edge * 0.5 + fres * 0.6) * _SurfaceGlow, a);
        }
        if (id < 7.5) // poison: sickly bubbling tint
        {
            float n = Noise(p * 6.0 + float3(0, -t * 0.7, t * 0.5));
            float blob = smoothstep(0.55, 0.75, n);
            float a = 0.12 + 0.25 * blob;
            return float4(c * a + c * blob * 0.35 * _SurfaceGlow, a);
        }
        // lightning: restrained crackle, re-rolled ~10x a second
        float tick = floor(t * 10.0);
        float n = Noise(p * 9.0 + tick * 3.17);
        float arc = 1.0 - smoothstep(0.0, 0.035, abs(n - 0.5));
        float on = step(0.55, Hash(float3(tick, 1.7, 2.9)));
        return float4(c * (arc * on * 1.4 + fres * 0.3) * _SurfaceGlow, 0.0);
    }

    // ---- passes -----------------------------------------------------------------------------------------------------
    v2f vertLine(appdata v)
    {
        v2f o = Shell(v, _Line, _MinPixels);
        if (_LineGlow <= 0.0) o.pos = float4(0, 0, -1, 1); // surface-only shell: no line
        return o;
    }

    v2f vertHalo(appdata v)
    {
        v2f o = Shell(v, _Line * _HaloWidth, _MinPixels * _HaloWidth);
        if (_Halo <= 0.0) o.pos = float4(0, 0, -1, 1); // degenerate: no fragments for magic
        return o;
    }

    v2f vertBody(appdata v) { return Shell(v, 0.0, 0.0); }

    float4 fragLine(v2f i) : SV_Target
    {
        float k = _LineGlow * Pulse() * BandAt(i.misc.y) * i.misc.x * i.misc.z * _Flash;
        float4 col = float4(_LineColor.rgb * k, 0);
        UNITY_APPLY_FOG_COLOR(i.fogCoord, col, float4(0, 0, 0, 0));
        return col;
    }

    float4 fragHalo(v2f i) : SV_Target
    {
        // Back faces of the wide hull: bright next to the body, soft toward the outer edge.
        float nv = abs(dot(normalize(i.wn), normalize(i.view)));
        float k = _Halo * pow(nv, 1.6) * Pulse() * BandAt(i.misc.y) * i.misc.x * i.misc.z * _Flash;
        float4 col = float4(_LineColor.rgb * k, 0);
        UNITY_APPLY_FOG_COLOR(i.fogCoord, col, float4(0, 0, 0, 0));
        return col;
    }

    float4 fragBody(v2f i) : SV_Target
    {
        float fres = pow(1.0 - saturate(dot(normalize(i.wn), normalize(i.view))), _RimPower);
        float4 s = Surface(_Surface, i.op, fres, _Time.y);
        float3 rim = _LineColor.rgb * (_RimGlow * fres * Pulse());
        float fade = i.misc.x * i.misc.z * _Flash;
        float4 col = float4((s.rgb + rim) * fade, s.a * fade);
        float4 fogCol = unity_FogColor * col.a; // premultiplied: no fog where nothing is drawn
        UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fogCol);
        return col;
    }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HULL"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One One
            CGPROGRAM
            #pragma vertex vertLine
            #pragma fragment fragLine
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "HALO"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One One
            CGPROGRAM
            #pragma vertex vertHalo
            #pragma fragment fragHalo
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Name "BODY"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vertBody
            #pragma fragment fragBody
            #pragma multi_compile_fog
            #pragma target 3.0
            ENDCG
        }
    }
    Fallback Off
}
