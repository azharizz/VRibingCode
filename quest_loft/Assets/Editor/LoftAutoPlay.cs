#if UNITY_EDITOR
// Computer-use Play driver: launched as GUI Unity with
//   -executeMethod LoftAutoPlay.EnterAndRun
// Opens Loft, enters REAL Play mode (StandupFlowTest runs), polls for
// Tools/play-result.txt, exits Play mode, then quits Unity with code.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LoftAutoPlay
{
    const int TIMEOUT_SECONDS = 180;

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

    public static void EnterAndRun()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity", OpenSceneMode.Single);
        var resultPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Tools/play-result.txt");
        try { if (System.IO.File.Exists(resultPath)) System.IO.File.Delete(resultPath); } catch { }
        EditorApplication.EnterPlaymode();
        double start = EditorApplication.timeSinceStartup;
        EditorApplication.update += Poll;
        void Poll()
        {
            bool done = System.IO.File.Exists(resultPath);
            bool timeout = EditorApplication.timeSinceStartup - start > TIMEOUT_SECONDS;
            if (!done && !timeout) return;
            EditorApplication.update -= Poll;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            Debug.Log(done ? "[AUTOPLAY] play-result.txt collected, leaving Play mode."
                           : "[AUTOPLAY] TIMEOUT waiting for play-result.txt");
            EditorApplication.Exit(done ? 0 : 1);
        }
    }
}
#endif
