// Morgott.Core IOverlayVfx.Overlay(): unlit additive line / x-ray template (consumer: Patcher StationLinks, bench
// placement rings, links and through-wall glow). Colour = 2 x _TintColor x vertex colour x _MainTex (AdditiveParticle
// convention), rgb premultiplied by alpha under Blend One One. _ZTest = CompareFunction (8 Always draws through walls,
// 4 LEqual is depth-tested). _Dash = period in metres along UV0.x (LineRenderer textureMode Tile), 0 = solid;
// _DashFill = lit share of each period. _DstBlend 1 One = additive (default), 10 OneMinusSrcAlpha = premultiplied
// alpha-over (keeps hue on bright ground). _Outline = dark rim share of each half-width across UV0.y (LineRenderer), 0 = off;
// _OutlineColor = its colour, alpha scaled by the line's alpha.
Shader "Morgott/Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (0.5, 0.5, 0.5, 0.5)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        _Dash ("Dash period (m)", Float) = 0
        _DashFill ("Dash fill", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        _Outline ("Outline", Range(0, 1)) = 0
        _OutlineColor ("Outline colour", Color) = (0, 0, 0, 0.85)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Lighting Off
            Blend One [_DstBlend], Zero One // dest alpha untouched in both modes

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _TintColor;
            float _Dash;
            float _DashFill;
            float _Outline;
            fixed4 _OutlineColor;

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
                float2 raw : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.raw = v.uv; // raw UV0: dash period stays in metres whatever _MainTex_ST tiles; .y across the line
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Dash > 0 && frac(i.raw.x / _Dash) > _DashFill) discard;
                fixed4 c = tex2D(_MainTex, i.uv) * _TintColor * 2 * i.color;
                if (_Outline > 0)
                {
                    float e = abs(i.raw.y * 2 - 1); // 0 centre, 1 edge
                    float aa = min(fwidth(i.raw.y) * 2, _Outline * 0.5); // ~1 px soft step, never wider than half the rim
                    float rim = smoothstep(1 - _Outline - aa, 1 - _Outline + aa, e);
                    c = lerp(c, fixed4(_OutlineColor.rgb, _OutlineColor.a * c.a), rim);
                }
                return fixed4(c.rgb * c.a, c.a); // premultiplied: Blend One One ignores a, One OneMinusSrcAlpha uses it
            }
            ENDCG
        }
    }
}
