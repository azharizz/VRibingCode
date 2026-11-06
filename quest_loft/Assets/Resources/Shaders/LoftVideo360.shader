// VIRTUAL LOFT — an equirectangular video seen from inside a sphere. UVs come from the view
// direction, so there is no seam or pole pinching from the mesh.
Shader "Loft/Video360"
{
    Properties { _MainTex ("Video", 2D) = "black" {} _Yaw ("Yaw offset 0..1", Float) = 0 }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" }
        Cull Front
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Yaw;
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float2 uv = float2(frac(atan2(d.x, d.z) / 6.2831853 + 0.5 + _Yaw), asin(clamp(d.y, -1, 1)) / 3.1415927 + 0.5);
                // tex2Dlod: the wrap at u=0/1 would pick the smallest mip and draw a seam
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            ENDCG
        }
    }
}
