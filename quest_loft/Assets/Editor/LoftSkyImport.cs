#if UNITY_EDITOR
// Panoramas become cubemaps: no trigonometry per pixel, and no seam. They keep their mips
// because the ocean reads a blurred level as its reflection.
using UnityEditor;

public class LoftSkyImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        var t = (TextureImporter)assetImporter;
        if (assetPath.Contains("/Resources/Detail/"))
        {
            t.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            t.mipmapEnabled = true;
            t.anisoLevel = 4;
            return;
        }
        if (!assetPath.Contains("/Resources/Sky/")) return;
        t.textureShape = TextureImporterShape.TextureCube;
        var cube = new TextureImporterSettings();
        t.ReadTextureSettings(cube);
        cube.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        cube.cubemapConvolution = TextureImporterCubemapConvolution.None;
        t.SetTextureSettings(cube);
        t.maxTextureSize = 4096;
        t.mipmapEnabled = true;
        t.sRGBTexture = true;
        t.npotScale = TextureImporterNPOTScale.None;
        var android = t.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 4096;
        android.format = TextureImporterFormat.ASTC_6x6;
        t.SetPlatformTextureSettings(android);
    }
}
#endif
