#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VirtualLoft.XR;
// NOTE: labels use legacy TextMesh + built-in Arial (not TextMeshPro):
// zero asset dependencies, renders in stripped player builds offline.

public static class LoftBootstrap
{
    // Spec anchors (FOCUS-LOFT-SPEC.md): desks + 4 boards on the main wall.
    static readonly Vector3[] DeskPos =
    {
        new Vector3(-2f, 0, 1.5f), new Vector3(0f, 0, 1.5f), new Vector3(2f, 0, 1.5f),
    };
    static readonly (string id, Vector3 pos, Vector2 size)[] BoardSpecs =
    {
        ("notes", new Vector3(-4.2f, 1.6f, 0f), new Vector2(2.4f, 1.4f)),
        ("kanban", new Vector3(-1.4f, 1.6f, 0f), new Vector2(2.8f, 1.6f)),
        ("calendar", new Vector3(1.6f, 1.6f, 0f), new Vector2(2.4f, 1.2f)),
        ("log", new Vector3(4.2f, 1.6f, 0f), new Vector2(2.0f, 1.4f)),
    };

    [MenuItem("Loft/Build Loft Scene")]
    public static void Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
        }

        // Cleanup (idempotent rebuild) — Board_* plates included: without
        // this every Build stacks another 4 plates/titles at the same spot and
        // stale (pre-fix) text wins the depth fight.
        foreach (var name in new[] { "Loft", "Scout", "Builder", "Keeper", "MainWall", "Floor",
            "Board_notes", "Board_kanban", "Board_calendar", "Board_log",
            "QuestPanel", "TelePad_0", "TelePad_1", "TelePad_2" })
        {
            GameObject old;
            while ((old = GameObject.Find(name)) != null) Object.DestroyImmediate(old);
        }

        // Seated eye-height camera (A28: seated default 1.2m per Comfort policy)
        var cam = GameObject.Find("Main Camera");
        if (cam == null)
        {
            cam = new GameObject("Main Camera");
            cam.AddComponent<Camera>();
        }
        cam.transform.position = new Vector3(0, Comfort.SEATED_EYE_HEIGHT, 6.5f);
        cam.transform.rotation = Quaternion.Euler(0, 180f, 0); // face -Z: wall + boards + agents (content side)
        var camComp = cam.GetComponent<Camera>();
        camComp.clearFlags = CameraClearFlags.SolidColor;
        camComp.backgroundColor = new Color(0.12f, 0.10f, 0.14f);

        if (GameObject.Find("Directional Light") == null)
        {
            var light = new GameObject("Directional Light");
            var l = light.AddComponent<Light>();
            l.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        // Warm floor + main curved-wall backdrop (flat = fine for Simulator gate)
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(2.4f, 1f, 2f);
        var floorMat = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.32f, 0.22f) };
        floor.GetComponent<Renderer>().material = floorMat;

        var wall = new GameObject("MainWall");
        var wallMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallMesh.name = "WallMesh";
        wallMesh.transform.SetParent(wall.transform, false);
        wallMesh.transform.position = new Vector3(0, 2f, -0.4f);
        wallMesh.transform.localScale = new Vector3(12f, 4f, 0.2f);
        var wallMat = new Material(Shader.Find("Standard")) { color = new Color(0.62f, 0.48f, 0.34f) };
        wallMesh.GetComponent<Renderer>().material = wallMat;

        // Loft root with all systems (incl. Comfort policy object for A28)
        var loft = new GameObject("Loft");
        var boards = loft.AddComponent<BoardManager>();
        var client = loft.AddComponent<LoftClient>();
        client.boards = boards;
        var agents = loft.AddComponent<AgentManager>();
        var voice = loft.AddComponent<VoiceCommands>();
        voice.testMode = true;
        var net = loft.AddComponent<Multiplayer>();
        var comfort = loft.AddComponent<Comfort>(); // teleport-only, seated, vignette
        var test = loft.AddComponent<StandupFlowTest>();
        test.agents = agents;
        test.boards = boards;
        test.voice = voice;
        test.net = net;
        var desk = loft.AddComponent<LoftDesk>();
        desk.agents = agents;
        desk.boards = boards;
        desk.net = net;
        loft.AddComponent<LoftQuestPanel>(); // world-space assign + teleport pads (self-builds in Awake)
        loft.AddComponent<LoftCompanyDemo>(); // player-only company shift (idle in editor)

        // 4 board plates on the main wall: title visible at 2m (A27)
        foreach (var (id, pos, size) in BoardSpecs)
            MakeBoardPlate(id, pos, size);

        // 3 agents at spec desks with floating name labels
        var names = new[] { "Scout", "Builder", "Keeper" };
        var ids = new[] { "scout", "builder", "keeper" };
        var colors = new[] { new Color(0.4f, 0.7f, 1f), new Color(0.5f, 1f, 0.5f), new Color(1f, 0.8f, 0.4f) };
        for (int i = 0; i < 3; i++)
        {
            var sm = MakeAgent(names[i], ids[i], DeskPos[i], colors[i]);
            agents.agents.Add(sm);
        }

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Loft.unity");
        Debug.Log("[LoftBootstrap] Saved Assets/Scenes/Loft.unity — open it and press Play.");
    }

    static AgentStateMachine MakeAgent(string goName, string id, Vector3 pos, Color color)
    {
        var go = new GameObject(goName);
        go.transform.position = pos;
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(go.transform, false);
        body.transform.localPosition = Vector3.up * 1f;
        body.GetComponent<Renderer>().material = new Material(Shader.Find("Standard")) { color = color };
        // Desk disc
        var desk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        desk.name = "Desk";
        desk.transform.SetParent(go.transform, false);
        desk.transform.localPosition = new Vector3(0, 0.4f, 0.9f);
        desk.transform.localScale = new Vector3(1.2f, 0.5f, 1.2f);
        // Floating name label: ~0.35m caps, readable at 2m+
        var label = new GameObject("Label");
        label.transform.SetParent(go.transform, false);
        label.transform.localPosition = new Vector3(0, 2.4f, 0);
        var tm = label.AddComponent<TextMesh>();
        tm.text = goName.ToUpperInvariant();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 64;
        tm.characterSize = 0.008f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        // Readable side faces the play side (+Z, camera at z=6.5 looking -Z).
        // Legacy TextMesh reads from -Z; without this the live view mirrors it.
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var sm = go.AddComponent<AgentStateMachine>();
        sm.agentId = id;
        return sm;
    }

    static void MakeBoardPlate(string id, Vector3 pos, Vector2 size)
    {
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = $"Board_{id}";
        plate.transform.position = new Vector3(pos.x, pos.y, pos.z + 0.05f);
        plate.transform.localScale = new Vector3(size.x, size.y, 0.08f);
        plate.GetComponent<Renderer>().material = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.16f, 0.17f, 0.22f),
        };
        var title = new GameObject("Title");
        title.transform.SetParent(plate.transform, false);
        title.transform.localPosition = new Vector3(0, 0.35f, 0.7f); // proud of plate face (+0.056 > half-depth 0.04), faces +Z (camera side)
        var tm = title.AddComponent<TextMesh>();
        tm.text = id.ToUpperInvariant();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 64;
        tm.characterSize = 0.008f; // large world-size type: Quest-blur-proof at 2m
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.95f, 0.8f);
        // Readable side faces the play side (+Z). Legacy TextMesh reads from
        // -Z; without this the live view mirrors every letter.
        title.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        // In-world content readout (LoftDesk.RefreshViews rewrites it live).
        // Sits proud of the plate face (world +0.056 > half-depth 0.04) and
        // faces the play side (+Z) — see note on Title above.
        var content = new GameObject("Content");
        content.transform.SetParent(plate.transform, false);
        content.transform.localPosition = new Vector3(0f, -0.12f, 0.7f);
        content.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var cm = content.AddComponent<TextMesh>();
        cm.text = $"{id}…";
        cm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        cm.fontSize = 40;
        cm.characterSize = 0.01f;
        cm.anchor = TextAnchor.MiddleCenter;
        cm.alignment = TextAlignment.Center;
        cm.color = new Color(0.85f, 0.92f, 1f);
    }
}
#endif
