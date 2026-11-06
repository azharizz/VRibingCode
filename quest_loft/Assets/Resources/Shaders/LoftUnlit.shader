// Unlit vertex-color shader for glow (windows, lanterns, stars, planets), soft
// alpha shapes (clouds, foam, light streaks) and the fade quad.
// _Blink > 0: vertex alpha is a per-quad seed and the quad flickers on/off.
// _Scroll: texture-free streak animation along object X (traffic), per pixel.
Shader "Loft/World/Unlit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _Gain ("Brightness", Float) = 1
        _Blink ("Blink rate", Float) = 0
        _Scroll ("Scroll speed", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst", Float) = 0
        [Enum(Off,0,On,1)] _ZW ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZT ("ZTest", Float) = 4
        _FogAdd ("Fog (0 off, 1 on, 2 additive)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Blend [_Src] [_Dst]
            ZWrite [_ZW]
            ZTest [_ZT]
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Gain, _Blink, _Scroll, _FogAdd;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 col : TEXCOORD0; float2 sc : TEXCOORD2; UNITY_FOG_COORDS(1) };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                fixed4 c = v.color;
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = GammaToLinearSpace(c.rgb);
                #endif
                if (_Blink > 0.0)
                {
                    float seed = c.a * 97.13;
                    float step_ = floor(_Time.y * _Blink * (0.15 + frac(seed * 3.7) * 0.5) + seed);
                    float h = frac(sin(step_ * 12.9898 + seed * 78.233) * 43758.5453);
                    c.rgb *= (h > 0.35) ? (0.75 + 0.5 * frac(seed * 7.3)) : 0.12;
                    c.a = 1.0;
                }
                o.sc = float2(v.vertex.x, c.a);
                if (_Scroll != 0.0) c.a = 1.0;
                o.col = c * _Tint;
                o.col.rgb *= _Gain;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = i.col;
                if (_Scroll != 0.0)
                {
                    float u = frac(i.sc.x * 0.07 - _Time.y * _Scroll + i.sc.y * 3.1);
                    c.rgb *= smoothstep(0.0, 0.08, u) * smoothstep(0.4, 0.2, u);
                }
                if (_FogAdd > 1.5) { UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0,0,0,0)); }
                else if (_FogAdd > 0.5) { UNITY_APPLY_FOG(i.fogCoord, c); }
                return c;
            }
            ENDCG
        }
    }
}
