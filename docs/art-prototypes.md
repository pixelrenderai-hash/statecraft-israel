# Art prototypes (opt-in)

Some art is trialled before it is admitted to the repository: AI-generated 3D models whose redistribution rights and
project licence are still under review. Such files never enter git, `ASSETS.yaml` or a shared build until they are
admitted. The code that shows them is opt-in, so a clean clone and a normal launch look and play exactly as before.

## Switches

| Switch | WebGL | Desktop player | Effect |
| --- | --- | --- | --- |
| Map Table UI | `?ui-preview=table` | `-ui-preview table` | Restyles the HUD, panels and Cabinet (`UiPreview.cs`, `UiPreview.uss`) |
| Building prototypes | `?building-preview` | `-building-preview` | Replaces settlement markers with prototype models, dresses transit camps and housing, and adds ships, an aircraft, a truck, a fort and figures (`BuildingPreview.cs`, `PreviewProps.cs`) |

Neither switch changes the simulation: the golden and smoke hashes in `docs/determinism.md` are the same with and
without them. Editor Play mode receives no URL, so it always shows the shipped look.

## Local prototype models

1. Keep the original exports outside `unity/Assets/`. Each model is a folder `<name>/fbx/` holding one `.fbx` and its
   `.fbm/` texture folder (one JPEG, or one PNG). The folder name is the prefab name; every such folder is imported.
   A `figure-` name (or any skinned model) keeps its rig and clips: a Generic rig, idle and walk clips looping, and an
   Animator whose default state is the idle clip. Its skinned bounds are fitted to the idle clip's first frame, since
   the importer's own bounds are not the mesh's. An export animated without a skin (a vehicle with moving parts) needs
   the `figure-` prefix too, or it imports static and its clips are dropped.
2. Import: `"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod PrototypeImport.Run -prototypeSource <folder>`.
   It writes `unity/Assets/Art/Settlements/Prototype/` (git-ignored): the models, URP Lit materials, animator controllers
   and prefabs under that folder's own `Resources/`. A re-run replaces the names it finds and keeps the others.
3. Check them without UI: `-executeMethod BuildingPreviewCapture.Run -captureDir <dir>` renders before/after pairs of
   each pose (each named place and prop in frame) and warns about a mapping none of whose prefabs loads; add
   `-captureTurntable` for each prefab alone from four sides (`<dir>/turntable/<name>-<yaw>.png`).

Every prefab is optional: a missing one keeps the box (or places nothing). What the switch shows, when the prefab is there:

| Where | Prefab (first that loads) |
| --- | --- |
| Tel Aviv, Jerusalem, Haifa | `tel-aviv-white-city-v1`, `jerusalem-stone-city-v1` (or `-7k`, `-3denhance`), `haifa-port-city-v1` |
| Petah Tikva, Rishon LeZion, Zikhron Ya'akov, Rosh Pinna, Rehovot, Hadera | `moshava-town-v1` |
| Every kibbutz, every moshav, every arab_town | `kibbutz-compound-v2` (else `kibbutz-dining-hall-v1`), `nahalal-moshav-v1`, `stone-village-v1` |
| A transit camp, a housing project the player builds | `maabara-camp-v1`, `shikun-housing-v1` |
| Props (`PreviewProps.All`; art dressing, approximate spots) | `altalena-lst-v1` off Tel Aviv, `immigrant-steamer-v1` in Haifa Bay, `airliner-c54-v1` over Lod airport, `sandwich-armoured-truck-v1` at Bab al-Wad, `tegart-fort-v1` at Latrun, `figure-pioneer-farmer-v1-tpose` (animated; else `figure-pioneer-farmer-v1`), `figure-immigrant-woman-v1`, `figure-soldier-1948-v1` |

Netanya, Tiberias and Safed keep their boxes. The props use a symbolic scale (a ship about 1.6 km long, a figure 350 m
tall) so they read at the 6-9 km zoom; ships and the aircraft bob gently in play mode. To add a prop, add one line to
`PreviewProps.All`; to map a settlement, one entry in `BuildingPreview`'s tables.

While that folder exists, every player build contains the models. Keep such a build local: `Build` refuses a
`-shareable` build, marks a WebGL build folder with `PENDING-RIGHTS-PROTOTYPES.txt`, and `tools/package_release.py`
refuses a marked folder. To admit a model later, record its source, licence and status in `ASSETS.yaml` and
`LICENSING.md`, move it out of the prototype folder, and make the placement a reviewed change.
