# loft-world notes

## Done (round 1, all verified by rendering screenshots in a scratch copy)
- 5 generated scenes (beach, scenery, city, galaxy, party = beach at night). Code: quest_loft/Assets/World/*.cs
  (Mk mesh kit, WorldCtx owns meshes/materials, WorldLife CPU animation, SceneKit shared pieces, *Sc.cs per scene).
- Shaders in Assets/Resources/Shaders (Loft/World/Lit, Unlit, Sky, Water). Resources so player builds keep them.
- LoftWorld: themes JSON (scene, sky, ambient trilight, light, fog, water, board, audio), Boards via Polar(bearing,dist,y)
  (+bearing = CEO's right), TMP board text (Title + Content children are TMP_Text), FadeToTheme, SetMood(k) for party,
  Resolve() so stale PlayerPrefs ids fall back to beach. Root object is still named "ThemeProps".
- TMP Essentials imported into Assets/TextMesh Pro; LiberationSans SDF set to Dynamic + source TTF embedded
  (missing glyphs: U+25B6, U+25B8, U+2713; the rest the UI uses is fine).
- Agents face +Z (imported prefabs wrapped in Model/Mesh rotated 180), scale 1.25, TMP nameplate with dark plate.
- QualitySettings Medium (Android default) MSAA 4.

## Numbers
draw calls <= 44, tris <= 62k (city), no leak over 3x5 scene switches. Board body text 1.31-1.35 deg, titles 1.6 deg, agent name 1.97, info 1.63.

## Lessons
- Mathf.SmoothStep(a,b,t) is NOT GLSL smoothstep (use (0,1,t)); exp2 fog at 1 km needs density <= 0.0008 or far terrain vanishes.
- FindObjectsByType skips HideFlags.DontSaveInEditor objects; use GameObject.Find("ThemeProps") to reach generated meshes.
- Generated objects use DontSaveInEditor in edit mode so LoftBootstrap does not bake them into Loft.unity.
- Edit-mode skips Update: LoftWorld ticks WorldLife once after ApplyTheme so poses are right in screenshots.
- Visible sun disc (view side) is decoupled from the key light (behind the CEO's shoulder) so agents/screens stay front-lit.
- Screenshot tool: Assets/World/Editor/WorldShot.cs (run in a scratch copy with a real GPU: no -nographics). Env LOFT_SHOT_*.
  Scratch copy: exclude Library/Bee (4 GB) when rsyncing.

## Weak / todo
- Palms read a bit like willow trees (leaflet triangles); thatch is shingle-like.
- Water is thin in the view (deck edge hides everything below -10 deg); only seen the seat pose carefully for scenery/city/galaxy.
- Not tested in Play mode / on device: FadeToTheme coroutine, SetMood during a live party, Quest perf, TMP in a player build.
- LoftParty banner position was designed for the old wall; now hangs at z=-0.25 (open air). Check visually in a party.
- Others to update: LoftVerify (theme names, ThemeProps child count, Content is TMP_Text not TextMesh), LoftBootstrap (defaultTheme, no MainWall/Floor).

## Integration lessons (lead, 2026-11-06)
- Edit-mode tests and renders miss Play-only bugs. Always check real Play mode.
- `Destroy` is deferred in Play: `while (Find(x)) Kill(x)` hangs the Editor forever. Deactivate first (Find skips inactive).
- DontSaveInEditor objects survive into Play but their materials do not: that gave magenta boards. BuildBoard now rebuilds on material mismatch.
- Weak art still open: palms read as willows, thatch is flat, sea is a thin band behind the deck.
