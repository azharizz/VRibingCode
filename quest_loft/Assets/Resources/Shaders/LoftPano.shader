// A photographic sky: two cubemaps cross-fading, graded per mood. Cubemaps, not panorama
// images, because reading one costs no trigonometry on every pixel of the sky.
Shader "Loft/World/Pano"
{
    Properties
    {
        _PanoA ("Sky A", Cube) = "grey" {}
        _PanoB ("Sky B", Cube) = "grey" {}
        _Blend ("A to B", Float) = 0
        _RotA ("Turn A (cos, sin)", Vector) = (1,0,0,0)
        _RotB ("Turn B (cos, sin)", Vector) = (1,0,0,0)
        _Exposure ("Exposure", Float) = 1
        _Sat ("Saturation", Float) = 1
        _Flash ("Lightning", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            samplerCUBE _PanoA, _PanoB;
            float4 _RotA, _RotB;
            float _Blend, _Exposure, _Sat, _Flash;

            struct vin { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(vin i)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(i.vertex);
                o.dir = i.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = i.dir;
                float3 c = texCUBE(_PanoA, float3(d.x * _RotA.x + d.z * _RotA.y, d.y, d.z * _RotA.x - d.x * _RotA.y)).rgb;
                if (_Blend > 0.001) // only while the weather is changing
                    c = lerp(c, texCUBE(_PanoB, float3(d.x * _RotB.x + d.z * _RotB.y, d.y, d.z * _RotB.x - d.x * _RotB.y)).rgb, _Blend);
                float g = dot(c, float3(0.3, 0.59, 0.11));
                c = lerp(g.xxx, c, _Sat) * _Exposure;
                c += _Flash * float3(0.75, 0.8, 1.0) * saturate(g * 3.0);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
