#if UNITY_EDITOR
// Dev tool: render every board with sample data to PNGs at VR scale.
//   Unity -batchmode -projectPath <copy> -executeMethod BoardGallery.Run
// Needs a GPU context (no -nographics). env: LOFT_GALLERY_DIR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualLoft.Panels;

public static class BoardGallery
{
    public static void Run()
    {
        try { Render(); }
        catch (Exception e) { Debug.LogError("[BoardGallery] " + e); }
        EditorApplication.Exit(0);
    }

    static void Render()
    {
        var dir = Environment.GetEnvironmentVariable("LOFT_GALLERY_DIR") ?? "gallery";
        Directory.CreateDirectory(dir);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.36f, 0.56f, 0.72f);
        cam.fieldOfView = 44f;
        cam.nearClipPlane = 0.05f;

        var state = PanelSample.Make();
        foreach (var type in new[] { "kanban", "review", "log", "notes", "project", "files", "diff", "preview" })
        {
            var b = LoftBoard.Create("g", type, state);
            if (type == "preview") { b.previewError = null; b.Refresh(); }
            b.transform.position = Vector3.zero;
            float need = Mathf.Max(b.H * 0.001f / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)),
                                   b.W * 0.001f / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.6f)) * 1.1f;
            cam.transform.SetPositionAndRotation(new Vector3(0, 0, -need), Quaternion.identity);
            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            File.WriteAllBytes(Path.Combine(dir, type + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(b.gameObject);
        }
        Debug.Log("[BoardGallery] done -> " + dir);
    }
}
#endif
