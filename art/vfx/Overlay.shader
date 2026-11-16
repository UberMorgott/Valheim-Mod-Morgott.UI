// Morgott.Core IOverlayVfx.Overlay(): unlit additive line / x-ray template (consumer: Patcher StationLinks, bench
// placement rings, links and through-wall glow). Colour = 2 x _TintColor x vertex colour x _MainTex (AdditiveParticle
// convention), rgb premultiplied by alpha under Blend One One. _ZTest = CompareFunction (8 Always draws through walls,
// 4 LEqual is depth-tested). _Dash = period in metres along UV0.x (LineRenderer textureMode Tile), 0 = solid;
// _DashFill = lit share of each period.
Shader "Morgott/Overlay"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (0.5, 0.5, 0.5, 0.5)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        _Dash ("Dash period (m)", Float) = 0
        _DashFill ("Dash fill", Range(0, 1)) = 0.5
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
            Blend One One

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
                float dash : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.dash = v.uv.x; // raw UV0.x: dash period stays in metres whatever _MainTex_ST tiles
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Dash > 0 && frac(i.dash / _Dash) > _DashFill) discard;
                fixed4 c = tex2D(_MainTex, i.uv) * _TintColor * 2 * i.color;
                return fixed4(c.rgb * c.a, 0);
            }
            ENDCG
        }
    }
}
