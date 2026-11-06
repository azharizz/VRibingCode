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
        public LoftInput inputOverride;          // tests / tools
        public LoftVoice voice;
        public LoftCard card;
        public LoftWrist wrist;
        public LoftTextEntry entry;
        public LoftMove move;

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
        public int pointMask = ~0;              // the cinema narrows this: nothing invisible can be hit
        public LoftCinema cinema;
        public static LoftInteraction Current { get; private set; }
        public bool TryRay(out Ray r) { r = default; if (input == null || !input.hasRay) return false; r = new Ray(input.rayOrigin, input.rayDir); return true; }
        GameObject cursor, ringHover, ringSel;
        LineRenderer laser;
        Renderer hoverPadRenderer;
        LoftButton holding;
        IPointerTarget ptrHover, ptrGrab;
        float holdTime;
        bool talkConsumed;
        string talkAgentId;
        int reqId;

        void Awake()
        {
            Current = this; Wire(); EnsureBuilt();
            if (GetComponent<LoftHead>() == null) gameObject.AddComponent<LoftHead>();
            cinema = GetComponent<LoftCinema>();
            if (cinema == null) cinema = gameObject.AddComponent<LoftCinema>();
            cinema.world = world; cinema.desk = desk; cinema.interaction = this;
        }

        // What stays usable while the world is hidden.
        public void KeepVisible(System.Action<Transform> show)
        {
            if (card != null) show(card.Root);
            if (wrist != null) show(wrist.Root);
            if (cursor != null) show(cursor.transform);
            if (laser != null) show(laser.transform);
        }

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
            move = Need(move);
            card.world = world;
            wrist.desk = desk; wrist.world = world; wrist.entry = entry; wrist.move = move;
            wrist.voiceStatus = VoiceStatus;
            wrist.say = Say;
            card.Build();
            // Teleport works anywhere now: the old fixed pads go.
            for (int i = 0; i < 3; i++)
            {
                var pad = GameObject.Find($"TelePad_{i}");
                if (pad != null) { pad.name = "dead"; LoftUtil.Kill(pad); } // (renamed first: Destroy is deferred in Play)
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
            // One thing at a time: no menu over the talk card (the Simulator also presses A and X together).
            bool busy = Talking || input.talkHeld || card.Visible;
            wrist.suppressed = busy;
            if (busy && wrist.IsOpen) wrist.SetOpen(false);
            if (input.wristDown && !entry.Active && !busy) { wrist.Toggle(); input.Haptic(0.2f, 0.03f); }
            wrist.Track(world.cam, input.hasLeftHand, input.leftPos, input.leftRot, xrOn);
            if (input.cancelDown) Cancel();

            if (cinema == null || !cinema.Active) move.Tick(input, world, comfort, dt);
            Point(dt);
            Talk();
            ShowNextSuggestion();
            if (voice.Recording) card.SetLevel(voice.Level);
        }

        // ---- B ---------------------------------------------------------------------------
        public void Cancel()
        {
            if (entry != null && entry.Active) { entry.Cancel(); return; }
            if (cinema != null && cinema.Active && !card.Visible) { cinema.Exit(); return; }
            if (move.Aiming) { move.AbortAim(); return; }
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
                hitSomething = Physics.Raycast(input.rayOrigin, input.rayDir, out hit, 30f, pointMask, QueryTriggerInteraction.Ignore);

            LoftButton newBtn = null; string newAgent = null; Renderer newPad = null;
            if (hitSomething)
                for (var t = hit.collider.transform; t != null; t = t.parent)
                {
                    var b = t.GetComponent<LoftButton>();
                    if (b != null) { newBtn = b; break; }
                    var a = t.GetComponent<AgentStateMachine>();
                    if (a != null) { newAgent = a.agentId; break; }
                }

            // IPointerTarget first: boards and such get raw trigger events.
            IPointerTarget newPt = null;
            if (hitSomething)
                for (var t = hit.collider.transform; t != null && newPt == null; t = t.parent) newPt = t.GetComponent<IPointerTarget>();
            if (!(newPt as Object)) newPt = null; // destroyed targets compare equal to null
            if (newPt != ptrHover)
            {
                if (ptrHover as Object != null) ptrHover.OnHover(false);
                ptrHover = newPt;
                if (ptrHover != null) { ptrHover.OnHover(true); input.Haptic(0.12f, 0.02f); }
            }
            var ray = new Ray(input.rayOrigin, input.rayDir);
            if (input.triggerDown && newPt != null && !BlockedByGui())
            {
                ptrGrab = newPt;
                ptrGrab.OnPress(ray, hit);
                input.Haptic(0.4f, 0.04f);
            }
            if (ptrGrab != null)
            {
                if (!(ptrGrab as Object)) ptrGrab = null;
                else if (input.triggerHeld && !input.triggerUp) ptrGrab.OnDrag(ray);
                else { ptrGrab.OnRelease(); ptrGrab = null; }
            }
            bool grabbing = ptrGrab != null || (input.triggerDown && newPt != null);

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
            if (input.triggerDown && hitSomething && !grabbing && !BlockedByGui())
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
                var agent = t.GetComponent<AgentStateMachine>();
                if (agent != null) { desk?.Select(agent.agentId); return true; }
            }
            return false;
        }

        public void TeleportTo(Vector3 ground)
        {
            Wire();
            move?.TeleportTo(world, comfort, ground);
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
                if (card.IsSuggestion) { DeferSuggestion(); StartTalk(); }
                else if (card.State == LoftCard.CardState.Proposal && !card.Risky) { talkConsumed = true; if (card.PressSend()) input.Haptic(0.5f, 0.05f); }
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
            DeferSuggestion();
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

        // ---- agent suggestions: idea card, trigger on ACCEPT only -------------------------------
        JObject shownSuggestion;

        void ShowNextSuggestion()
        {
            if (desk == null || card.Visible || Talking || voice.Recording || reqPending) return;
            var sg = desk.TakeSuggestion();
            if (sg == null) return;
            ShowSuggestion(sg);
        }

        public void ShowSuggestion(JObject sg)
        {
            shownSuggestion = sg;
            string id = (string)sg["id"];
            card.onSend = () => { shownSuggestion = null; desk.AnswerSuggestion(id, true); };
            card.onCancel = () => { shownSuggestion = null; desk.AnswerSuggestion(id, false); };
            card.ShowSuggestion(AgentName((string)sg["agentId"]), (string)sg["title"], (string)sg["reason"]);
        }

        // Something else needs the card (A pressed, a voice answer): the idea waits its turn instead of being lost.
        void DeferSuggestion()
        {
            if (shownSuggestion == null || !card.IsSuggestion) return;
            desk.RequeueSuggestion(shownSuggestion);
            shownSuggestion = null;
            card.Hide();
        }

        bool reqPending => card.State == LoftCard.CardState.Thinking;

        void Say(string s) { desk?.Note(s); if (card != null && !card.Visible) card.ShowResult(s); }
        string AgentName(string id) => desk?.agents?.Find(id)?.GetComponent<AgentSkin>()?.displayName ?? id;

        void OnDisable()
        {
            if (voice != null && voice.Recording) voice.Cancel();
            Talking = false;
        }
    }
}
