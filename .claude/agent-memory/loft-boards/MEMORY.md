# loft-boards notes

## Done (compiles in scratch copy; nothing rendered or run in Play yet)
- Assets/Panels/PanelTheme.cs: tokens (PT: colors, status accents, type scale, spacing) + generated sprites (9-slice rounded fill/stroke, shadow, circle, ring).
- Assets/Panels/PanelKit.cs: parts on uGUI (top-left mm coords): Box, Flat, Outline, Dot, Line, Text, Mono (<mspace>), Avatar, Pill, Badge, Progress, Tab, Divider, icons from shapes, Measure/Fit.
- Assets/Panels/PanelData.cs: PanelState + PanelSample.Make() sample data.

## Not started
LoftBoard frame (IPointerTarget, grab/resize/close, hit regions via RectTransform list), 7 board types, LoftPanels (Open/Close/CloseAll, layout save, off-view pause, preview cap), LoftClient.Panels.cs, LoftPreviewScreen/LoftDesk/LoftWorld board rewiring, Editor/BoardGallery.cs. No PNGs yet (scratch gallery/ is empty).

## Design decisions
- One world uGUI Canvas per board (1 unit = 1 mm, scale 0.001, rotated 180 Y so root +Z faces viewer) => few draw calls. Resize = uniform root scale. Body font 54-56 mm (about 1.2 deg at 3 m).
- Hover per element: read LoftInteraction.CursorPos while hovered. Hold-to-confirm done in OnDrag. Mono text via <mspace>.
- Sizes planned: kanban 2400x1200, review 1400x1250, log 1200x1300, preview 1800x1250, notes 1500x1100, files 1900x1150, diff 2000x1250.

## Lessons
- BSD sed -i fails here: use Edit. Scratch: scratchpad/boards, sync.sh then Unity batchmode (no -nographics). Compile currently blocked only by loft-world AgentSkin.cs GetInstanceID (transient).

## Next
Write LoftBoard + Kanban + BoardGallery, render, critique PNGs, iterate; then remaining types, LoftPanels, live wiring.

## Lead finished the boards solo (2026-11-06)
LoftBoard (frame, grab, resize, close), BoardViews (7 boards), LoftPanels (open, close, layout per scene, feed, preview), LoftClient.Panels, BoardGallery. Rendered and reviewed; "open the kanban board" ran live. NOT yet tried live: drag, resize, close, layout return after scene change, clicking a real preview.
