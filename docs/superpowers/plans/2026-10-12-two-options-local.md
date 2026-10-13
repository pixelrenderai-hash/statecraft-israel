# Two local play options — 12 October 2026

Spec: `../specs/2026-10-12-look-and-play-two-options-design.md`.
The user explicitly requests both options built now. This supersedes the earlier implementation ordering gate, while all wider rollout, asset spending and publication gates remain in place.

## Shared constraints

- Preserve the default path and deterministic sim; no edits under sim or content.
- Code-built faceted geometry; no downloaded art or paid 3D jobs.
- Both experiences use a fresh shared seed and never read or write the campaign save.
- Command takes precedence when both option flags appear. Removing flags restores normal play next launch.
- Local only; do not push, deploy, share or make a shareable build.

## Work packages

1. Implement Map Look using the detailed Step 0 plan. Test deterministic placement, water/slope/settlement exclusions, resource ownership and grade restoration.
2. Living War Room (`?experience=war-room`, `-experience war-room`): isolated IDisposable presentation reading SimBridge; period command HUD forwards explicit player actions to the existing buttons, report/units/turn tour, skip and sound controls. Test state immutability, navigation forwarding and attach/dispose.
3. Command (`?prototype=command`, `-prototype command`): explicitly throwaway standalone economy and mission, unit selection/orders, harvest/build/escort/threats, pause/restart, independent input and HUD. No simulation references. Test economy, pause, construction, objectives and mission completion.
4. Root integrates both after normal presenter setup, gates saves, owns disposal, prepares concept images and a local chooser.
5. Root verifies the complete Unity suite, repository checks, local WebGL build, deterministic golden/smoke checks and real browser interactions/captures. Fresh reviewer inspects code and mode isolation.

## Acceptance

Both URLs launch playable, visually distinct experiences over the shared improved map. A is the unchanged campaign with added presentation; B is labeled a local prototype. Captures are clearly separated from generated concepts. Record actual results and any limits in `docs/local-ui-preview.md`.

## Execution results

- Implemented all three packages and isolated GameRoot integration. Procedural meshes and original synthesized audio avoid new art dependencies. Command takes precedence; removing the flags restores normal play. Both comparisons protect the campaign save.
- Pinned Unity full suite: 559/559 passed. After the final browser-driven HUD/mesh fixes, the focused Command/WarRoom/Hud gate passed 56/56. Simulation 625/625, policy 150/150, tools 178 passed and one skipped; tools used available Python 3.11 wheels because the pinned raster environment is unavailable here. Repository policy check passed over 805 files.
- Final local WebGL build succeeded with zero errors, retaining the pending-rights marker. Build payload grew about 0.8 MiB. Ten browser cases passed the pinned 1950/1952 goldens and five-turn smoke hash: all eight map/building/table combinations, plus A and B.
- Actual controls verified A navigation, end-turn, dispatch skip and fresh restart; B pause, harvest, construction, rescue, delivery and restart. A complete B mission reached victory in 112 seconds using keyboard/right-click controls, without injecting model state.
- Actual captures reviewed at 1920×1080 and 1280×720, with Astra implementation and render review. Generated concepts are separately labelled. The local chooser includes both play links, controls, matched map captures and browser-only playtest ratings.
- Isolated 1920×1080 map profile: all-on median/p95 3.71/5 ms at gameplay03, 12.50/14 ms at diorama02. Summed positive median differences for scatter/towns/lines are 0.52/1.14 ms, below the 2.5 ms target. The raw overview sum is -0.58 ms, within measurement noise; no speedup is claimed.

No push, PR, shareable package or deployment. Visual approval remains with the user. This is a local comparison: symbolic campaign movement, synthesized sound, fictional Command rules and direct movement without terrain pathfinding remain deliberate prototype limits. Cold startup and final goal-layout regression results are recorded in the local preview notes.
