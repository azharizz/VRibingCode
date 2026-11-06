#if UNITY_EDITOR
// Computer-use Play driver: launched as GUI Unity with
//   -executeMethod LoftAutoPlay.EnterAndRun
//   -executeMethod LoftAutoPlay.EnterStayShoot
// Opens Loft, enters REAL Play mode, screenshots the game view, quits.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LoftAutoPlay
{
    // EnterStayShoot: Play + capture the GAME view itself to
    // Tools/gameview.png (window-manager-proof), then quit.
    public static void EnterStayShoot()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
        double start = EditorApplication.timeSinceStartup;
        bool shot = false;
        string shotPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Tools/gameview.png");
        EditorApplication.update += Poll;
        void Poll()
        {
            double el = EditorApplication.timeSinceStartup - start;
            if (!shot && EditorApplication.isPlaying && el > 8)
            {
                shot = true;
                ScreenCapture.CaptureScreenshot(shotPath);
                Debug.Log("[AUTOPLAY] game-view screenshot taken.");
            }
            if (shot && (System.IO.File.Exists(shotPath) || el > 60))
            {
                EditorApplication.update -= Poll;
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                EditorApplication.Exit(System.IO.File.Exists(shotPath) ? 0 : 1);
            }
        }
    }
}
#endif
