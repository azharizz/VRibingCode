// Procedural sky for a Skybox material: zenith -> mid -> horizon gradient, a
// mirrored ground color, sun disc + halo + horizon glow, optional cheap stars
// (hashed cells), a milky-way band and two soft nebula blobs. No textures,
// no loops: ~40 ALU per pixel.
Shader "Loft/World/Sky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.2,0.45,0.8,1)
        _Mid ("Mid", Color) = (0.45,0.7,0.95,1)
        _Horizon ("Horizon", Color) = (1,0.85,0.65,1)
        _Below ("Below horizon", Color) = (1,0.85,0.65,1)
        _SunDir ("Sun direction", Vector) = (0,0.2,-1,0)
        _SunColor ("Sun color", Color) = (1,0.9,0.7,1)
        _SunSize ("Sun size (radians)", Float) = 0.045
        _Glow ("Horizon glow", Float) = 0.6
        _Stars ("Stars", Float) = 0
        _Neb1 ("Nebula A", Color) = (0.5,0.1,0.7,1)
        _Neb2 ("Nebula B", Color) = (0.1,0.4,0.8,1)
        _Neb ("Nebula amount", Float) = 0
        _Exposure ("Exposure", Float) = 1
        _Rot ("Rotation (radians)", Float) = 0
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
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Mid, _Horizon, _Below, _SunColor, _Neb1, _Neb2;
            float4 _SunDir;
            float _SunSize, _Glow, _Stars, _Neb, _Exposure, _Rot;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.dir = v.xyz;
                return o;
            }

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float starLayer(float3 d, float scale, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float3 f = frac(p) - 0.5;
                float h = hash(cell);
                float3 off = float3(hash(cell + 7.1), hash(cell + 13.7), hash(cell + 3.3)) - 0.5;
                float r = length(f - off * 0.5);
                float on = step(1.0 - density, h);
                float tw = 0.65 + 0.35 * sin(_Time.y * (1.0 + h * 3.0) + h * 40.0);
                return on * smoothstep(0.16, 0.0, r) * tw * (0.5 + h);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float s = sin(_Rot), c = cos(_Rot);
                float3 dr = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z); // star/nebula rotation only
                float h = d.y;
                float3 col;
                if (h >= 0.0)
                {
                    float a = saturate(h);
                    col = lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.22, a));
                    col = lerp(col, _Zenith.rgb, smoothstep(0.18, 0.85, a));
                }
                else col = lerp(_Horizon.rgb, _Below.rgb, smoothstep(0.0, -0.12, h));

                float3 sd = normalize(_SunDir.xyz);
                float dd = dot(d, sd);
                float glow = pow(saturate(dd), 6.0) * exp(-abs(h) * 5.0) * _Glow;
                glow += pow(saturate(dd), 40.0) * 0.5 * _Glow;
                col += _SunColor.rgb * glow;
                float disc = smoothstep(cos(_SunSize * 1.15), cos(_SunSize * 0.9), dd);
                col = lerp(col, _SunColor.rgb * 1.8, disc);

                if (_Stars > 0.0 || _Neb > 0.0)
                {
                    float up = smoothstep(-0.05, 0.1, h);
                    if (_Neb > 0.0)
                    {
                        float band = exp(-pow(dot(dr, normalize(float3(0.3, 0.8, 0.5))) * 3.2, 2.0));
                        float n1 = 0.5 + 0.5 * sin(dr.x * 4.1 + sin(dr.y * 5.3 + dr.z * 2.1) * 1.7 + dr.z * 3.3);
                        float n2 = 0.5 + 0.5 * sin(dr.z * 3.7 - dr.y * 4.4 + sin(dr.x * 3.1) * 2.0);
                        float3 neb = _Neb1.rgb * n1 * band + _Neb2.rgb * n2 * band * 0.9;
                        neb += _Neb1.rgb * pow(saturate(dot(dr, normalize(float3(-0.7, 0.35, -0.6)))), 7.0) * 0.8;
                        col += neb * _Neb * up;
                    }
                    if (_Stars > 0.0)
                    {
                        float st = starLayer(dr, 55.0, 0.09) + starLayer(dr, 110.0, 0.06) * 0.8 + starLayer(dr, 26.0, 0.1) * 1.5;
                        col += st * _Stars * up * float3(1.0, 0.96, 0.9);
                    }
                }
                return fixed4(col * _Exposure, 1);
            }
            ENDCG
        }
    }
}
