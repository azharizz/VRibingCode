// VIRTUAL LOFT — LoftInteraction.cs: the CEO's controls, headset AND desktop.
//
//   right trigger  point + select  (hover highlight, cursor dot, haptic ticks)
//   hold A         talk; release sends the WAV to POST /voice
//   right stick    30 degree snap turn (one turn per flick, brief dim)
//   B              cancel / back
//   X (or look at the left wrist)  wrist menu
//
// Input comes from LoftInput: XrInput in a headset, DesktopInput on the Mac
// (mouse aims, click = trigger, hold V = A, Esc/C = B, Q/E turn, M wrist). Both
// feed this one class, so the desktop rig tests the real thing.

using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace VirtualLoft.XR
{
    [DisallowMultipleComponent]
    public class LoftInteraction : MonoBehaviour
    {
        public static readonly Vector3[] PadPos =
        {
            new Vector3(-3f, 0.03f, 4.6f),
            new Vector3(0f, 0.03f, 5.2f),
            new Vector3(3f, 0.03f, 4.6f),
        };

        public LoftInput inputOverride;          // tests / tools
        public LoftVoice voice;
        public LoftCard card;
        public LoftWrist wrist;
        public LoftTextEntry entry;

        public string HoveredAgentId { get; private set; }
        public LoftButton HoveredButton { get; private set; }
        public bool Talking { get; private set; }
        public Vector3 CursorPos => cursor != null ? cursor.transform.position : Vector3.zero;

        LoftDesk desk;
        LoftWorld world;
        Comfort comfort;
        LoftClient client;
        readonly XrInput xr = new XrInput();
        readonly DesktopInput desktop = new DesktopInput();
        LoftInput input;
        GameObject cursor, ringHover, ringSel;
        LineRenderer laser;
        Renderer hoverPadRenderer;
        LoftButton holding;
        float holdTime;
        bool talkConsumed;
        string talkAgentId;
        int reqId;

        void Awake() { Wire(); EnsureBuilt(); }

        void Wire()
        {
            if (desk == null) desk = FindFirstObjectByType<LoftDesk>();
            if (world == null) world = FindFirstObjectByType<LoftWorld>();
            if (comfort == null) comfort = FindFirstObjectByType<Comfort>();
            if (client == null) client = FindFirstObjectByType<LoftClient>();
        }

        // Idempotent; also driven from the editor (bootstrap / verify).
        public void EnsureBuilt()
        {
            Wire();
            voice = Need(voice);
            card = Need(card);
            wrist = Need(wrist);
            entry = Need(entry);
            card.world = world;
            wrist.desk = desk; wrist.world = world; wrist.entry = entry;
            wrist.voiceStatus = VoiceStatus;
            wrist.say = Say;
            card.Build();
            for (int i = 0; i < PadPos.Length; i++)
            {
                var pad = GameObject.Find($"TelePad_{i}");
                if (pad != null && (pad.transform.Find("Disk") == null || pad.transform.Find("Label") != null)) { LoftUtil.Kill(pad); pad = null; }
                if (pad != null) continue;
                pad = new GameObject($"TelePad_{i}");
                pad.transform.position = PadPos[i];
                // Subtle floor marker: no label.
                LoftUtil.Prim(PrimitiveType.Cylinder, "Disk", pad.transform, Vector3.zero, new Vector3(0.7f, 0.015f, 0.7f), new Color(0.35f, 0.55f, 0.65f), keepCollider: true);
            }
        }

        T Need<T>(T c) where T : Component
        {
            if (c != null) return c;
            var found = GetComponent<T>();
            return found != null ? found : gameObject.AddComponent<T>();
        }

        // ---- frame ------------------------------------------------------------------
        void Update()
        {
            Wire();
            if (world == null || world.cam == null) return;
            input = inputOverride ?? (XRSettings.isDeviceActive ? (LoftInput)xr : desktop);
            input.Poll(world);
            Tick(Time.unscaledDeltaTime);
        }

        public void Tick(float dt)
        {
            Wire();
            if (inputOverride != null) input = inputOverride;
            if (input == null || world == null || world.cam == null) return;
            bool xrOn = XRSettings.isDeviceActive;

            if (input.snap != 0) { comfort?.SnapTurn(input.snap, world.cam.transform); input.Haptic(0.2f, 0.03f); }
            if (input.wristDown && !entry.Active) { wrist.Toggle(); input.Haptic(0.2f, 0.03f); }
            wrist.Track(world.cam, input.hasLeftHand, input.leftPos, input.leftRot, xrOn);
            if (input.cancelDown) Cancel();

            Point(dt);
            Talk();
            if (voice.Recording) card.SetLevel(voice.Level);
        }

        // ---- B ---------------------------------------------------------------------------
        public void Cancel()
        {
            if (entry != null && entry.Active) { entry.Cancel(); return; }
            if (voice.Recording) { voice.Cancel(); Talking = false; talkConsumed = true; card.Hide(); return; }
            if (card.Visible) { reqId++; card.PressCancel(); return; } // also drops an in-flight request
            if (wrist.IsOpen) { wrist.Back(); return; }
            if (desk != null && desk.selectedAgentId != null) desk.Select(null);
        }

        // ---- pointing ----------------------------------------------------------------------
        void Point(float dt)
        {
            RaycastHit hit = default;
            bool hitSomething = false;
            if (input.hasRay && !BlockedByGui())
                hitSomething = Physics.Raycast(input.rayOrigin, input.rayDir, out hit, 30f, ~0, QueryTriggerInteraction.Ignore);

            LoftButton newBtn = null; string newAgent = null; Renderer newPad = null;
            if (hitSomething)
                for (var t = hit.collider.transform; t != null; t = t.parent)
                {
                    var b = t.GetComponent<LoftButton>();
                    if (b != null) { newBtn = b; break; }
                    var a = t.GetComponent<AgentStateMachine>();
                    if (a != null) { newAgent = a.agentId; break; }
                    if (t.name.StartsWith("TelePad_")) { newPad = t.GetComponentInChildren<Renderer>(); break; }
                }

            bool changed = newBtn != HoveredButton || newAgent != HoveredAgentId || newPad != hoverPadRenderer;
            if (changed)
            {
                if (HoveredButton != null) HoveredButton.SetHover(false);
                if (hoverPadRenderer != null) hoverPadRenderer.sharedMaterial.color = new Color(0.35f, 0.55f, 0.65f);
                HoveredButton = newBtn; HoveredAgentId = newAgent; hoverPadRenderer = newPad;
                if (HoveredButton != null) HoveredButton.SetHover(true);
                if (hoverPadRenderer != null) hoverPadRenderer.sharedMaterial.color = new Color(0.55f, 0.95f, 1f);
                if (newBtn != null || newAgent != null || newPad != null) input.Haptic(0.12f, 0.02f); // hover tick
                if (holding != null && holding != HoveredButton) { holding.HoldProgress = 0f; holding = null; }
            }

            DrawPointer(hitSomething, hit, newBtn != null || newAgent != null || newPad != null);
            UpdateRings();

            // Click
            if (input.triggerDown && hitSomething && !BlockedByGui())
            {
                if (HoveredButton != null && HoveredButton.holdSeconds > 0f) { holding = HoveredButton; holdTime = 0f; input.Haptic(0.15f, 0.02f); }
                else if (Click(hit.collider.gameObject)) input.Haptic(0.5f, 0.05f);
            }
            if (holding != null)
            {
                if (input.triggerHeld && holding == HoveredButton)
                {
                    holdTime += dt;
                    holding.HoldProgress = Mathf.Clamp01(holdTime / holding.holdSeconds);
                    if (holdTime >= holding.holdSeconds)
                    {
                        var b = holding; holding = null; b.HoldProgress = 0f;
                        LoftAudio.Play("sfx_click", 0.8f);
                        input.Haptic(0.8f, 0.08f);
                        b.Press();
                    }
                }
                else { holding.HoldProgress = 0f; holding = null; }
            }
        }

        bool BlockedByGui()
        {
            if (entry != null && entry.Active) return true;
            if (XRSettings.isDeviceActive || inputOverride != null || desk == null) return false;
            var gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return desk.overlayRect.Contains(gui);
        }

        // Routes a pointed-at collider to the right action (public for tests / tools).
        public bool Click(GameObject go)
        {
            Wire();
            for (var t = go.transform; t != null; t = t.parent)
            {
                var b = t.GetComponent<LoftButton>();
                if (b != null) { if (!b.Enabled) return false; LoftAudio.Play("sfx_click", 0.8f); b.Press(); return true; }
                if (t.name.StartsWith("TelePad_") && int.TryParse(t.name.Substring(8), out int pad)) { TeleportTo(pad); return true; }
                var agent = t.GetComponent<AgentStateMachine>();
                if (agent != null) { desk?.Select(agent.agentId); return true; }
            }
            return false;
        }

        public void TeleportTo(int i)
        {
            Wire();
            if (world == null || world.rig == null || i < 0 || i >= PadPos.Length) return;
            if (comfort != null) comfort.BeginTeleport();
            var rig = world.rig;
            rig.SetPositionAndRotation(new Vector3(PadPos[i].x, rig.position.y, PadPos[i].z), Quaternion.Euler(0f, 180f, 0f));
            if (comfort != null) comfort.EndTeleport();
            LoftAudio.PlayAt("sfx_teleport", PadPos[i] + Vector3.up * 1.2f, 0.8f);
        }

        // ---- cursor, laser, rings ----------------------------------------------------------
        static Material Sprite(Color c)
        {
            var sh = Shader.Find("Sprites/Default");
            return new Material(sh != null ? sh : Shader.Find("Standard")) { color = c };
        }

        GameObject Disc(string name, PrimitiveType type, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            LoftUtil.Kill(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = Sprite(c);
            return go;
        }

        void DrawPointer(bool hit, RaycastHit h, bool interactive)
        {
            if (cursor == null) cursor = Disc("PointerDot", PrimitiveType.Sphere, Color.white);
            if (laser == null)
            {
                laser = new GameObject("PointerLaser").AddComponent<LineRenderer>();
                laser.positionCount = 2;
                laser.widthMultiplier = 0.004f;
                laser.sharedMaterial = Sprite(new Color(0.4f, 0.9f, 1f, 0.7f));
                laser.numCapVertices = 2;
            }
            bool show = input.hasRay && !BlockedByGui();
            cursor.SetActive(show && hit);
            laser.enabled = show;
            if (!show) return;
            var end = hit ? h.point : input.rayOrigin + input.rayDir * 4f;
            laser.SetPosition(0, input.handPos);
            laser.SetPosition(1, end);
            laser.startColor = laser.endColor = interactive ? new Color(0.5f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
            if (hit)
            {
                float d = Vector3.Distance(input.rayOrigin, h.point);
                float s = Mathf.Clamp(d * 0.011f, 0.008f, 0.06f) * (interactive ? 1.4f : 1f);
                cursor.transform.position = h.point + h.normal * 0.004f;
                cursor.transform.localScale = Vector3.one * s;
                cursor.GetComponent<Renderer>().sharedMaterial.color = interactive ? new Color(0.45f, 1f, 1f) : new Color(1f, 1f, 1f, 0.55f);
            }
        }

        void UpdateRings()
        {
            if (desk == null) return;
            PlaceRing(ref ringSel, "SelectedRing", desk.selectedAgentId, new Color(1f, 0.82f, 0.25f, 0.9f), 1.0f + 0.05f * Mathf.Sin(Time.unscaledTime * 4f));
            PlaceRing(ref ringHover, "HoverRing", HoveredAgentId == desk.selectedAgentId ? null : HoveredAgentId, new Color(0.45f, 0.95f, 1f, 0.75f), 0.9f);
        }

        void PlaceRing(ref GameObject ring, string name, string agentId, Color c, float size)
        {
            var a = agentId != null && desk.agents != null ? desk.agents.Find(agentId) : null;
            if (a == null) { if (ring != null) ring.SetActive(false); return; }
            if (ring == null) ring = Disc(name, PrimitiveType.Cylinder, c);
            ring.SetActive(true);
            ring.transform.position = new Vector3(a.transform.position.x, a.transform.position.y + 0.02f, a.transform.position.z);
            ring.transform.localScale = new Vector3(size, 0.004f, size);
        }

        // ---- voice ---------------------------------------------------------------------------
        void Talk()
        {
            if (input.talkDown)
            {
                if (card.State == LoftCard.CardState.Proposal && !card.Risky) { talkConsumed = true; if (card.PressSend()) input.Haptic(0.5f, 0.05f); }
                else if (card.State == LoftCard.CardState.Proposal) { talkConsumed = true; Say("This one needs the red button. A can't confirm it."); }
                else if (card.State == LoftCard.CardState.Thinking) talkConsumed = true;
                else StartTalk();
            }
            if (Talking && (input.talkUp || !input.talkHeld || voice.HitMax)) EndTalk();
            if (!input.talkHeld) talkConsumed = false;
        }

        public string VoiceStatus()
        {
            if (voice == null) return "Voice: not available";
            voice.CheckPermission(false);
            if (desk == null || !desk.Connected) return "Voice: backend offline";
            if (voice.permission == MicPermission.Denied) return "Voice: microphone blocked";
            if (!voice.HasDevice) return "Voice: no microphone";
            return "Voice: ready (hold A)";
        }

        void StartTalk()
        {
            if (desk == null) return;
            talkAgentId = HoveredAgentId ?? desk.selectedAgentId;
            voice.CheckPermission(true);
            var why = voice.Problem();
            if (why == null && !desk.Connected)
                why = $"The backend is offline ({client?.serverUrl}). Start it, then hold A and try again.";
            if (why == null) why = voice.Begin();
            if (why != null) { card.ShowError(why); input.Haptic(0.6f, 0.1f); return; }
            Talking = true;
            card.ShowListening(talkAgentId == null ? null : AgentName(talkAgentId));
            input.Haptic(0.3f, 0.04f);
        }

        void EndTalk()
        {
            Talking = false;
            var take = voice.End();
            input.Haptic(0.2f, 0.03f);
            if (take.tap) { card.Hide(); desk?.Note("Hold A while you speak, release when done."); return; }
            if (take.problem != null) { card.ShowError(take.problem); return; }
            string target = talkAgentId == null ? null : AgentName(talkAgentId);
            card.ShowThinking(target);
            int my = ++reqId;
            client.ThenAny(client.Voice(take.wav, talkAgentId, desk.projectId, world.Current?.id), r => OnReply(my, r));
        }

        // Typed phrase through the same pipeline (debug strip, tests).
        public void SendText(string text)
        {
            Wire();
            if (client == null || string.IsNullOrWhiteSpace(text)) return;
            talkAgentId = HoveredAgentId ?? desk?.selectedAgentId;
            card.ShowThinking(talkAgentId == null ? null : AgentName(talkAgentId));
            int my = ++reqId;
            client.ThenAny(client.VoiceText(text, talkAgentId, desk?.projectId, world?.Current?.id), r => OnReply(my, r));
        }

        void OnReply(int my, LoftClient.VoiceReply r)
        {
            if (my != reqId) return; // cancelled while thinking
            if (r == null) { card.ShowError("No answer from the backend."); return; }
            if (r.error != null) { card.ShowError(r.error); return; }
            HandleResponse(r.json);
        }

        // The backend's answer -> immediate action, or the card. Public for tests.
        public IntentRoute HandleResponse(JObject resp)
        {
            var route = LoftIntent.Route(resp);
            string transcript = (string)resp?["transcript"], summary = (string)resp?["summary"];
            var intent = resp?["intent"] as JObject;
            switch (route)
            {
                case IntentRoute.Invalid:
                    card.ShowError($"I did not understand the backend's answer (action '{LoftIntent.Action(intent) ?? "missing"}').", transcript);
                    break;
                case IntentRoute.Immediate:
                    desk.ExecuteIntent(intent, false);
                    card.ShowResult(desk.LastIntentMessage, transcript);
                    break;
                default:
                    bool risky = route == IntentRoute.Risky;
                    card.onCancel = () => desk.Note("Cancelled, nothing was sent.");
                    card.onSend = () =>
                    {
                        bool ok = desk.ExecuteIntent(intent, true);
                        if (ok) card.ShowResult(desk.LastIntentMessage, transcript); else card.ShowError(desk.LastIntentMessage, transcript);
                    };
                    card.ShowProposal(transcript, summary, risky);
                    break;
            }
            return route;
        }

        void Say(string s) { desk?.Note(s); if (card != null && !card.Visible) card.ShowResult(s); }
        string AgentName(string id) => desk?.agents?.Find(id)?.GetComponent<AgentSkin>()?.displayName ?? id;

        void OnDisable()
        {
            if (voice != null && voice.Recording) voice.Cancel();
            Talking = false;
        }
    }
}
