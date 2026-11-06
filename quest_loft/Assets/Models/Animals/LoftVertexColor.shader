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

        struct Input
        {
            float4 vcol;
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
        }
        ENDCG
    }
    Fallback "Diffuse"
}
