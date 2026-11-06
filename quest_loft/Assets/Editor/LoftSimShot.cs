#if UNITY_EDITOR
// One-shot Simulator evidence run (GUI editor only, NOT batchmode):
//   Unity -projectPath quest_loft -executeMethod LoftSimShot.Run
// Enters Play, lets XR init against Meta XR Simulator, captures an
// in-game screenshot + writes Tools/sim-shot-result.txt, exits Play, quits.
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LoftSimShot
{
    const string kGo = "Tools/sim-shot.go";
    static double enteredAt;
    static double lastTry;

    // Static ctor re-runs after every domain reload: resubscribe so a
    // compile triggered by our own edit can't strand the run.
    static LoftSimShot() { EditorApplication.update += Tick; }

    public static void Run()
    {
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), kGo), "go");
        enteredAt = 0;
        lastTry = 0;
    }

    static void Tick()
    {
        if (!System.IO.File.Exists(
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), kGo))) return;
        if (!EditorApplication.isPlaying)
        {
            // Retry entering Play until compiles settle.
            double now = EditorApplication.timeSinceStartup;
            if (enteredAt == 0) enteredAt = -1; // mark waiting
            if (now - lastTry > 3 && !EditorApplication.isCompiling)
            {
                lastTry = now;
                var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                if (!sc.name.Equals("Loft"))
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Loft.unity");
                else
                    EditorApplication.EnterPlaymode();
            }
            return;
        }
        if (enteredAt <= 0) enteredAt = EditorApplication.timeSinceStartup;
        double el = EditorApplication.timeSinceStartup - enteredAt;
        if (el < 25) return;
        try { System.IO.File.Delete(
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), kGo)); } catch { }
        try
        {
            var desk = Object.FindAnyObjectByType<LoftDesk>();
            string state = "NO-DESK";
            if (desk != null)
            {
                desk.Assign("builder", "simulator proof task");
                state = $"tasks={desk.tasks.Count} boards={(desk.boards != null ? desk.boards.ListCards("kanban").Count : -1)}";
            }
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Tools");
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "sim-shot.png"));
            File.WriteAllText(Path.Combine(dir, "sim-shot-result.txt"),
                $"[SIMSHOT] waited={el:F0}s {state} xrDisplayActive={XRDisplayActive()}\n");
            Debug.Log($"[SIMSHOT] done {state}");
        }
        catch (System.Exception e) { Debug.LogError($"[SIMSHOT] FAILED: {e.Message}"); }
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    static string XRDisplayActive()
    {
        try
        {
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) if (d.running) return "YES-" + d.subsystemDescriptor.id;
            return displays.Count == 0 ? "NO-subsystems" : "NO-notrunning";
        }
        catch { return "ERR"; }
    }
}
#endif
