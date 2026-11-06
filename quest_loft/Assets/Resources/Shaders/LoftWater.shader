// Stylized animated water: 3 gerstner-style waves in the vertex shader, faceted
// normals from screen derivatives, fresnel blend to the sky horizon color, sun
// glitter, white crests, exp2 fog. No textures.
Shader "Loft/World/Water"
{
    Properties
    {
        _Shallow ("Shallow color", Color) = (0.2,0.75,0.8,1)
        _Deep ("Deep color", Color) = (0.03,0.28,0.5,1)
        _SkyH ("Sky horizon", Color) = (1,0.85,0.65,1)
        _SkyZ ("Sky up", Color) = (0.4,0.7,0.95,1)
        _SunDir ("Sun direction", Vector) = (0,0.2,-1,0)
        _SunColor ("Sun color", Color) = (1,0.9,0.7,1)
        _Amp ("Wave height", Float) = 0.18
        _Speed ("Wave speed", Float) = 1
        _Glint ("Glitter", Float) = 1.5
        _Foam ("Crest foam", Float) = 0.5
        _Exposure ("Exposure", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+1" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Shallow, _Deep, _SkyH, _SkyZ, _SunColor;
            float4 _SunDir;
            float _Amp, _Speed, _Glint, _Foam, _Exposure;

            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float hgt : TEXCOORD1; UNITY_FOG_COORDS(2) };

            float wave(float2 p, float2 dir, float freq, float spd, float t)
            {
                return sin(dot(p, dir) * freq + t * spd);
            }

            v2f vert(float4 v : POSITION)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v).xyz;
                float t = _Time.y * _Speed;
                float d = length(wp.xz - _WorldSpaceCameraPos.xz);
                float fade = saturate(1.2 - d / 90.0); // far waves flatten: no shimmering at the horizon
                float h = wave(wp.xz, normalize(float2(0.2, 1.0)), 0.55, 1.1, t) * 0.5
                        + wave(wp.xz, normalize(float2(-0.6, 0.8)), 0.9, 1.5, t) * 0.3
                        + wave(wp.xz, normalize(float2(0.9, 0.35)), 1.6, 2.1, t) * 0.2;
                wp.y += h * _Amp * fade;
                o.hgt = h * fade;
                o.wp = wp;
                o.pos = UnityWorldToClipPos(wp);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.wp), ddx(i.wp)));
                if (n.y < 0) n = -n;
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float ndv = saturate(dot(n, vdir));
                float fres = pow(1.0 - ndv, 3.0);
                float dist = length(_WorldSpaceCameraPos - i.wp);
                float3 body = lerp(_Shallow.rgb, _Deep.rgb, saturate(dist / 60.0));
                body = lerp(body, body * 1.25, saturate(i.hgt * 0.7 + 0.35)); // lit faces of waves
                float3 r = reflect(-vdir, n);
                float3 sky = lerp(_SkyH.rgb, _SkyZ.rgb, saturate(r.y * 2.5));
                float3 col = lerp(body, sky, saturate(fres + 0.08));
                float3 sd = normalize(_SunDir.xyz);
                float g = pow(saturate(dot(r, sd)), 60.0);
                col += _SunColor.rgb * g * _Glint;
                col += _SunColor.rgb * pow(saturate(dot(r, sd)), 6.0) * 0.12;
                float crest = smoothstep(0.55, 0.9, i.hgt) * _Foam;
                col = lerp(col, float3(1,1,1), crest * (1.0 - saturate(dist / 70.0)));
                fixed4 c = fixed4(col * _Exposure, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
