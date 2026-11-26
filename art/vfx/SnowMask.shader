// Morgott.Core ISnowVfx.SnowMask(): updates the R8 snow trail mask (consumer: Patcher SnowTrails).
// Pass 0 (Graphics.Blit previous -> next): next(uv) = saturate(previous(uv + _Shift.xy) - _Fade), 0 where uv + _Shift
// leaves 0..1: re-centre by whole texels and refill in one blit. Pass 1 (GL quads in 0..1 space): UV0 = quad-local -1..1
// (y = walk direction), vertex colour R = depth 0..1; soft footprint ellipse, BlendOp Max. Pass 2 (GL quads, ARGBHalf):
// colour A >= 0.5 = soft-edged rectangle / rotated OBB (full inside |uv.x| < colour G and |uv.y| < colour B, 0 at quad edge);
// colour A < 0.5 = radial circle for fire melt / leaf disc (full inside length(uv) < colour G, 0 at quad edge);
// outputs float4(shelter * _MaskMeta.z, roofRelY_or_-1000, leaf * _MaskMeta.y, 0) via BlendOp Max + ColorMask RGB.
Shader "Morgott/Snow Mask"
{
    Properties
    {
        _MainTex ("Previous mask", 2D) = "black" {}
        _Shift ("Shift (uv)", Vector) = (0, 0, 0, 0)
        _Fade ("Fade per blit", Float) = 0
        _MaskMeta ("Mask meta (roofRelY, leafStrength, isRoofOrFire, unused)", Vector) = (500, 0, 1, 0)
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Shift;
            float _Fade;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv + _Shift.xy;
                float inside = all(uv >= 0) && all(uv <= 1) ? 1 : 0;
                return saturate(tex2D(_MainTex, uv).r - _Fade) * inside;
            }
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed depth : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.depth = v.color.r;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Narrower heel half, wider toe half; soft rim so overlapping prints merge into a trail.
                float2 q = i.uv * float2(i.uv.y > 0 ? 1 : 1.25, 1);
                return (1 - smoothstep(0.45, 1, length(q))) * i.depth;
            }
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _MaskMeta;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 inner = i.color.gb;
                float2 k = saturate((abs(i.uv) - inner) / max(1.0 - inner, 1e-3));
                float shape = (i.color.a < 0.5)
                    ? (1.0 - smoothstep(0.0, 1.0, saturate((length(i.uv) - i.color.g) / max(1.0 - i.color.g, 1e-3))))
                    : (1.0 - smoothstep(0.0, 1.0, max(k.x, k.y)));
                return float4(shape * i.color.r * _MaskMeta.z, (shape > 0.05 && _MaskMeta.z > 0.5) ? _MaskMeta.x : -1000.0, shape * _MaskMeta.y, 0.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
