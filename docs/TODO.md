# Virtual Loft to-do (lead works solo, then one Sonnet teammate does QA)

## Code
- [x] 1. Combined compile + check of the round 2 tree
- [x] 2. Rebuild beach for the 26x18 plan: deck, posts, roof, furniture, wider sea, real palms
- [x] 3. Rebuild scenery, city, galaxy for the plan
- [x] 4. Board frame: grab, resize, close (IPointerTarget)
- [x] 5. LoftPanels: open, close, layout saved per scene, off-view pause, preview cap
- [x] 6. Seven boards: kanban, review, log, preview, notes, files, diff
- [x] 7. Board API client + live data; remove fixed boards and old LoftDesk board text
- [x] 8. BoardGallery renders, then design pass
- [x] 9. LoftBridge to direct calls (reflection is unsafe under IL2CPP)
- [x] 10. Speech: large-v3-turbo, stt-compare tool, sample saving, faster-whisper Docker server
- [x] 11. README and DEPLOY for new endpoints, rebuild Docker image
- [x] 12. XR Simulator runs through a Mac build, see docs/XR-SIMULATOR.md. Buttons still to verify
- [x] 13. Update LoftVerify, run everything green

## QA (one Sonnet teammate, testing only)
- [x] 14. QA pass done (backend, automated checks, code review); bugs fixed. Live drag, resize, close, scene-return in the Editor still to try

## Needs the user or hardware
- [ ] Real Quest: frame rate, comfort, controller aim
- [ ] Compare speech models on the user's own voice
- [ ] Cloud deploy to GCP or AWS
