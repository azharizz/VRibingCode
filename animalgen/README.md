# animalgen — the loft's animals, made 100% in code

Blender (headless) builds each animal from a recipe: low-poly mesh with
vertex colors, a 19-bone skeleton, skin weights and 5 clips that match
`AgentSkin`'s Animator `State`: **Idle 0 · Working 1 · Done 2 · Failed 3 · Party 4**
(Party = 120 BPM dance: bounce on the beat, hip sway, paws up, happy ^^ eyes).

```bash
brew install --cask blender     # once
./animalgen/make.sh fox         # build -> test -> preview
```

| file | does |
| --- | --- |
| `recipes.py` | shape + colors per animal (add a new animal here) |
| `build.py` | mesh, skeleton, weights, clips, FBX export to `quest_loft/Assets/Models/Animals/<id>.fbx` |
| `test.py` | 60+ checks: mesh budget/size/facing, weights, per-frame deformation (floor, planted feet, pops, joint limits, seamless loops, blink, L/R symmetry), FBX round-trip vs source, Unity axis settings |
| `preview.py` | `out/<id>/turntable.png`, `<Clip>_sheet.png` contact sheets, `<Clip>.mp4` |
| `party_preview.py` | the whole PARTY MODE moment with all 8 animals (EEVEE): `out/party/party.mp4`, `party_sheet.png`, `party_hero.png`. Same layout as Unity's `LoftParty` |

Unity picks the FBX up automatically (`Assets/Editor/LoftAnimalImport.cs`):
Generic rig + Bake Axis Conversion, clip loop flags, a 4-state Animator,
and `Resources/Skins/<id>.prefab`, which agents with `skin: "<id>"` use.
`LoftVerify.RunAll` re-checks it inside Unity (State clips, faces +Z,
not mirrored, height, material).

Animations are math functions of the clip phase in `build.py`
(`clip_idle`, `clip_working`, `clip_done`, `clip_failed`). Rotations are
written in body axes: +X = lean/nod forward, +Y = tilt to the animal's
right, +Z = turn to its left.
