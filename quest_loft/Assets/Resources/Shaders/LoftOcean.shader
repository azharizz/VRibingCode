// The ocean under a photographic sky. Smooth normals from the wave math plus fine ripples,
// and it reflects the panorama itself: one blurred texture read instead of a real reflection.
Shader "Loft/World/Ocean"
{
    Properties
    {
        _Shallow ("Shallow color", Color) = (0.1,0.6,0.65,1)
        _Deep ("Deep color", Color) = (0.03,0.28,0.5,1)
        _SunDir ("Sun direction", Vector) = (0,0.2,-1,0)
        _SunColor ("Sun color", Color) = (1,0.9,0.7,1)
        _Amp ("Wave height", Float) = 0.18
        _Speed ("Wave speed", Float) = 1
        _Glint ("Glitter", Float) = 1.5
        _Foam ("Crest foam", Float) = 0.5
        _Exposure ("Exposure", Float) = 1
        _PanoA ("Sky A", Cube) = "grey" {}
        _PanoB ("Sky B", Cube) = "grey" {}
        _Blend ("A to B", Float) = 0
        _RotA ("Turn A (cos, sin)", Vector) = (1,0,0,0)
        _RotB ("Turn B (cos, sin)", Vector) = (1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Shallow, _Deep, _SunColor;
            float4 _SunDir;
            float _Amp, _Speed, _Glint, _Foam, _Exposure, _Blend;
            float4 _RotA, _RotB;
            samplerCUBE _PanoA, _PanoB;

            struct vin { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float hgt : TEXCOORD1; float2 swell : TEXCOORD2; UNITY_FOG_COORDS(3) UNITY_VERTEX_OUTPUT_STEREO };

            static const float2 D0 = float2(0.196, 0.981), D1 = float2(-0.6, 0.8), D2 = float2(0.932, 0.363);

            v2f vert(vin i)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, i.vertex).xyz;
                float t = _Time.y * _Speed;
                float fade = saturate(1.2 - length(wp.xz - _WorldSpaceCameraPos.xz) / 90.0);
                float h = sin(dot(wp.xz, D0) * 0.55 + t * 1.1) * 0.5
                        + sin(dot(wp.xz, D1) * 0.9 + t * 1.5) * 0.3
                        + sin(dot(wp.xz, D2) * 1.6 + t * 2.1) * 0.2;
                // The swell's slope is smooth, so the vertices can work it out for every pixel.
                o.swell = (D0 * (cos(dot(wp.xz, D0) * 0.55 + t * 1.1) * 0.275)
                         + D1 * (cos(dot(wp.xz, D1) * 0.9 + t * 1.5) * 0.27)
                         + D2 * (cos(dot(wp.xz, D2) * 1.6 + t * 2.1) * 0.32)) * (_Amp * fade);
                wp.y += h * _Amp * fade;
                o.hgt = h * fade;
                o.wp = wp;
                o.pos = UnityWorldToClipPos(wp);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float3 Sky(float3 d)
            {
                float3 c = texCUBElod(_PanoA, float4(d.x * _RotA.x + d.z * _RotA.y, d.y, d.z * _RotA.x - d.x * _RotA.y, 5.5)).rgb;
                if (_Blend > 0.001)
                    c = lerp(c, texCUBElod(_PanoB, float4(d.x * _RotB.x + d.z * _RotB.y, d.y, d.z * _RotB.x - d.x * _RotB.y, 5.5)).rgb, _Blend);
                return c;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y * _Speed;
                float2 p = i.wp.xz;
                float dist = length(_WorldSpaceCameraPos - i.wp);
                // Fine ripples on the swell, gone before they could shimmer at a distance.
                float near = saturate(1.0 - dist / 140.0);
                float2 g = i.swell + float2(cos(p.x * 5.1 + p.y * 2.7 + t * 2.7), cos(p.y * 5.7 - p.x * 2.3 - t * 2.3)) * (0.03 * near);
                float3 n = normalize(float3(-g.x, 1.0, -g.y));

                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float fres = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, vdir)), 5.0);
                float3 r = reflect(-vdir, n);
                r.y = abs(r.y) + 0.06; // above the panorama's horizon band, which would streak
                float3 body = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 45.0));
                body *= 0.85 + 0.3 * saturate(i.hgt * 0.7 + 0.5);
                float3 col = lerp(body, Sky(normalize(r)), saturate(fres + 0.12));
                float3 sd = normalize(_SunDir.xyz);
                float rs = saturate(dot(r, sd));
                col += _SunColor.rgb * (pow(rs, 240.0) * 3.0 + pow(rs, 24.0) * 0.18) * _Glint;
                float crest = smoothstep(0.6, 0.95, i.hgt) * _Foam * (1.0 - saturate(dist / 70.0));
                col = lerp(col, float3(0.9, 0.92, 0.95), crest);
                fixed4 c = fixed4(col * _Exposure, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
