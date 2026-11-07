// Cheap lit shader for generated world meshes: vertex colors, trilight ambient,
// one directional light (half-lambert, per vertex), exp2 fog, optional wind sway.
// Vertex alpha = rigidity (1 = rigid, 0 = fully swaying), so palms/trees sway in
// the vertex shader at zero CPU cost.
Shader "Loft/World/Lit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
        _SwayAmp ("Sway amplitude", Float) = 0
        _SwaySpeed ("Sway speed", Float) = 1.2
        _Emit ("Emission add", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Tint, _Emit;
            float _SwayAmp, _SwaySpeed;
            // Set globally by the weather; all zero in every other scene.
            float4 _LoftRoof;       // x min, x max, z min, z max
            float _LoftRoofY, _LoftRoofTop, _LoftDetailAmt;
            fixed4 _LoftWarm;       // the lanterns' light under the roof
            sampler2D _LoftDetail;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; fixed3 amb : TEXCOORD0; fixed3 sun : TEXCOORD1; float3 wp : TEXCOORD2; float3 nrm : TEXCOORD3; fixed3 alb : TEXCOORD4; UNITY_FOG_COORDS(5) UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float sw = 1.0 - v.color.a;
                if (_SwayAmp > 0.0)
                {
                    float ph = _Time.y * _SwaySpeed + wp.x * 0.31 + wp.z * 0.23;
                    float s = sw * sw;
                    wp.x += sin(ph) * _SwayAmp * s;
                    wp.z += cos(ph * 0.83 + 1.3) * _SwayAmp * 0.6 * s;
                    wp.y += sin(ph * 1.7) * _SwayAmp * 0.15 * s;
                }
                o.pos = UnityWorldToClipPos(wp);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz) * 0.5 + 0.5);
                ndl *= ndl;
                float3 amb = n.y > 0 ? lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, n.y)
                                     : lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, -n.y);
                float3 alb = v.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                alb = GammaToLinearSpace(alb);
                #endif
                alb *= _Tint.rgb;
                o.amb = alb * amb + _Emit.rgb;
                o.sun = alb * _LightColor0.rgb * ndl;
                o.wp = wp; o.nrm = n; o.alb = alb;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 amb = i.amb, sun = i.sun;
                if (_LoftRoofY > 0.0)
                {
                    // Where the sun's line through this point meets the roof: an exact shadow, no shadow map.
                    float3 l = _WorldSpaceLightPos0.xyz;
                    float2 h = i.wp.xz + l.xz * ((_LoftRoofY - i.wp.y) / max(l.y, 0.06));
                    float2 inside = smoothstep(-0.25, 0.25, h - _LoftRoof.xz) * smoothstep(-0.25, 0.25, _LoftRoof.yw - h);
                    float under = step(i.wp.y, _LoftRoofY - 0.05);
                    sun *= 1.0 - inside.x * inside.y * under;
                    float2 o = smoothstep(-1.5, 1.5, i.wp.xz - _LoftRoof.xz) * smoothstep(-1.5, 1.5, _LoftRoof.yw - i.wp.xz);
                    float room = o.x * o.y * step(i.wp.y, _LoftRoofTop); // up to the ridge: the roof's underside is in the room
                    amb *= 1.0 - 0.3 * o.x * o.y * under; // less sky reaches under a roof
                    amb += i.alb * _LoftWarm.rgb * room * (0.55 + 0.45 * saturate(i.nrm.y * 0.5 + 0.5));
                }
                float3 col = amb + sun;
                if (_LoftDetailAmt > 0.0)
                {
                    float3 a = abs(normalize(i.nrm));
                    float2 uv = a.y > a.x && a.y > a.z ? i.wp.xz * float2(0.16, 0.55) : a.x > a.z ? i.wp.zy * float2(0.55, 0.16) : i.wp.xy * float2(0.55, 0.16);
                    float sand = step(i.wp.y, -0.06) * step(0.5, a.y);
                    float g = tex2D(_LoftDetail, uv + sand * uv * 5.0).g;
                    float close = saturate(1.4 - length(i.wp - _WorldSpaceCameraPos) / 30.0); // never on far mountains or towers
                    col *= lerp(1.0, clamp(g * 2.6, 0.55, 1.3), _LoftDetailAmt * close * (1.0 - sand * 0.65));
                }
                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
