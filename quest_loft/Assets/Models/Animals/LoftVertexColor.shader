// Lit shader for the generated animals: color comes from mesh vertex colors
// (no textures). Vertex colors are authored in sRGB, so convert in Linear.
Shader "Loft/VertexColorLit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 150

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        #pragma target 3.0

        fixed4 _Tint;
        // The room's lantern light, set by the weather. Black everywhere else.
        float4 _LoftRoof;
        float _LoftRoofTop;
        fixed4 _LoftWarm;

        struct Input
        {
            float4 vcol;
            float3 worldPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vcol = v.color;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 c = IN.vcol.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
            c = GammaToLinearSpace(c);
            #endif
            o.Albedo = c * _Tint.rgb;
            float2 room = smoothstep(-1.5, 1.5, IN.worldPos.xz - _LoftRoof.xz) * smoothstep(-1.5, 1.5, _LoftRoof.yw - IN.worldPos.xz);
            o.Emission = o.Albedo * _LoftWarm.rgb * (room.x * room.y * step(IN.worldPos.y, _LoftRoofTop) * 0.8);
        }
        ENDCG
    }
    Fallback "Diffuse"
}
