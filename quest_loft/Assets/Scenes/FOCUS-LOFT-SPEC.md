# Focus Loft — Scene Spec (Simulator-ready, virtual-only)

- Room: warm wooden loft, lo-fi rain + soft light. 1 main curved wall 12 m wide (holds 6–12 boards, V1 uses 4). Side walls = archive. No Scene Mesh / Spatial Anchors / camera APIs.
- Desks (3): desk-1 Scout (-2.0, 0, 1.5), desk-2 Builder (0, 0, 1.5), desk-3 Keeper (+2.0, 0, 1.5). Capsule placeholders in P0, Mixamo anims in V3.
- Boards (4 on main wall): Notes (-4.2, 1.6) 2.4×1.4 · Kanban (-1.4, 1.6) 2.8×1.6 (ToDo/Doing/Done) · Calendar strip (+1.6, 1.6) 2.4×1.2 · Agent log (+4.2, 1.6) 2.0×1.4. Positions mirror `LoftLayout.json`.
- Readability: font ≥32sp at 2 m, high contrast, no small laptop text (Quest blurs small text).
- Interaction: controller/hand ray → hover highlight ring + name tag; trigger = select/grab; pinch = resize; every voice command has button fallback.
- Voice (10 V1, push-to-talk on trigger hold): wake / run […] / stop / move that to wall / summarize this / assign to [name] / remind at […] / next / done / focus mode.
- Comfort: teleport only (no smooth locomotion), seated default, vignette on move, 15–30 min sessions. Target 72 fps.
- Multiplayer: Photon room 2–6 users, shared rays + avatars + live board edits; CEO assigns, member moves own; activity log = who moved what.
