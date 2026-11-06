// VIRTUAL LOFT — marks the finished frame fully opaque. Translucent UI leaves alpha below 1,
// and the XR Simulator shows its own environment through it. Touches alpha only.
Shader "Hidden/Loft/AlphaOne"
{
    SubShader
    {
        Tags { "Queue" = "Overlay+1000" "RenderType" = "Overlay" }
        ZTest Always
        ZWrite Off
        Cull Off
        ColorMask A
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = float4(v.vertex.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0); // the quad fills the screen
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return fixed4(0, 0, 0, 1); }
            ENDCG
        }
    }
}
