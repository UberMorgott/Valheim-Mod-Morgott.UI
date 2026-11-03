// Vaettir boss AoE telegraph (Modules/AoeTelegraph.cs, spec 2026-10-28 "telegraph v2").
// One mesh, two submeshes: 0 = fill (_Mode 0), 1 = rim (_Mode 1). Per instance through a MaterialPropertyBlock:
// _Draw (outline sweep 0..1), _Progress (fill 0..1), _Flash (strike pulse 0..1), _Alpha (fade), _DepthBias (m).
// UV0.x = fill coord f (0 = origin / boss side, 1 = far edge), UV0.y = perimeter param p (outline sweep),
// UV1.x = across the rim (0 = the area's edge, 1 = inside).
// Technique mirrored from MorgottTweaks [AuraVisuals]: queue 3010 (after transparent VFX), ZWrite Off, ZTest LEqual,
// Cull Off, premultiplied alpha blend, unlit, no fog; the eye pull moves only the DEPTH of each vertex toward the
// camera (screen position untouched), so grass / swamp clutter right above the ground stops hiding it while walls,
// hills and bodies still do. Contrast is LDR (dark outer band vs bright inner line); the HDR factor is a bloom bonus.
// The depth math is mirrored by TelegraphMath.PulledDepth (unit tested).
Shader "Morgott/Telegraph"
{
    Properties
    {
        _Mode ("Mode (0 fill, 1 rim)", Float) = 0
        _Draw ("Outline", Range(0, 1)) = 1
        _Progress ("Fill", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
        _Alpha ("Alpha", Range(0, 1)) = 1
        _DepthBias ("Depth bias (m)", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Lighting Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Mode;
            float _Draw;
            float _Progress;
            float _Flash;
            float _Alpha;
            float _DepthBias;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 coord : TEXCOORD0; // f, p, across
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                float3 vp = mul(UNITY_MATRIX_V, float4(wp, 1.0)).xyz;
                float4 clip = mul(UNITY_MATRIX_P, float4(vp, 1.0));
                // Depth-only eye pull (TelegraphMath.PulledDepth): the pull fades in from 0.5 m to 2 m view depth and the
                // pulled depth never crosses the near plane.
                float depth = -vp.z;
                float dist = max(length(vp), 1e-4);
                float fade = smoothstep(0.5, 2.0, depth);
                float k = max(0.5, (dist - _DepthBias * fade) / dist);
                float pulled = max(depth * k, min(depth, _ProjectionParams.y + 0.01));
                float4 pc = mul(UNITY_MATRIX_P, float4(vp.xy * (pulled / max(depth, 1e-4)), -pulled, 1.0));
                clip.z = pc.z / pc.w * clip.w;
                o.pos = clip;
                o.coord = float3(v.uv0.x, v.uv0.y, v.uv1.x);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float f = i.coord.x;
                float p = i.coord.y;
                float c = i.coord.z;
                float3 rgb;
                float a;
                if (_Mode > 0.5)
                {
                    // Rim: dark outer band + bright warm inner line, drawn along the perimeter up to _Draw.
                    float shown = step(p, _Draw + 1e-4);
                    float inner = smoothstep(0.48, 0.58, c);
                    rgb = lerp(float3(0.08, 0.0, 0.0), float3(1.0, 0.82, 0.55) * 1.3, inner);
                    a = lerp(0.75, 1.0, inner) * shown;
                    rgb = lerp(rgb, float3(1.0, 0.55, 0.4) * 1.6, _Flash * inner);
                }
                else
                {
                    // Fill: faint tint once the outline is closed, the filled part from the origin, a bright advancing front.
                    float closed = step(0.9999, _Draw);
                    float filled = step(f, _Progress + 1e-4) * step(1e-4, _Progress);
                    float front = filled * step(_Progress - 0.04, f);
                    rgb = float3(1.0, 0.12, 0.04) * (1.0 + 0.8 * front);
                    a = max(0.10 * closed, 0.42 * filled);
                    a = lerp(a, 0.65, _Flash);
                    rgb = lerp(rgb, float3(1.0, 0.45, 0.3) * 1.5, _Flash);
                }
                a *= _Alpha;
                return float4(rgb * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
