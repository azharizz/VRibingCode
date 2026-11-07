// Rain: one mesh of streaks, wrapped around the viewer in the vertex shader. Each quad's
// position holds its random seed. Streaks inside _Dry (under the roof) simply aren't drawn.
Shader "Loft/World/Rain"
{
    Properties
    {
        _Rain ("Amount", Float) = 0
        _Color ("Color", Color) = (0.75,0.8,0.9,1)
        _Dry ("Dry area (x min, x max, z min, z max)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Rain;
            fixed4 _Color;
            float4 _Dry;

            struct vin { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float a : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(vin i)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 s = i.vertex.xyz;
                const float3 box = float3(34, 15, 34);
                float3 c = _WorldSpaceCameraPos;
                float fall = frac(s.y - _Time.y * (0.85 + s.x * 0.3));
                float3 p;
                p.xz = frac((s.xz * box.xz - c.xz) / box.xz) * box.xz + c.xz - box.xz * 0.5;
                p.y = -1.0 + fall * box.y;
                p.x += fall * 1.4; // wind
                bool dry = p.x > _Dry.x && p.x < _Dry.y && p.z > _Dry.z && p.z < _Dry.w;
                float on = (s.z < _Rain && !dry) ? 1.0 : 0.0;
                float3 side = normalize(cross(float3(0, 1, 0), c - p)) * 0.011;
                p += side * (i.uv.x * 2.0 - 1.0) + float3(0.09, 0.62, 0) * i.uv.y;
                p *= 1.0;
                o.pos = UnityWorldToClipPos(p);
                o.pos *= on; // collapses to nothing
                o.uv = i.uv;
                o.a = saturate(length(c - p) / 3.0) * saturate(_Rain * 2.0);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float edge = 1.0 - abs(i.uv.x * 2.0 - 1.0);
                return fixed4(_Color.rgb * (edge * i.uv.y * 0.22 * i.a), 1);
            }
            ENDCG
        }
    }
}
