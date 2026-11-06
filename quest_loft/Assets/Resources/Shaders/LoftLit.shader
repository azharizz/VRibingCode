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
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Tint, _Emit;
            float _SwayAmp, _SwaySpeed;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed3 col : TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert(appdata v)
            {
                v2f o;
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
                o.col = alb * (amb + _LightColor0.rgb * ndl) + _Emit.rgb;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = fixed4(i.col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
