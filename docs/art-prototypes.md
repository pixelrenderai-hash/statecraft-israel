# Art prototypes (opt-in)

Some art is trialled before it is admitted to the repository: AI-generated 3D models whose redistribution rights and
project licence are still under review. Such files never enter git, `ASSETS.yaml` or a shared build until they are
admitted. The code that shows them is opt-in, so a clean clone and a normal launch look and play exactly as before.

## Switches

| Switch | WebGL | Desktop player | Effect |
| --- | --- | --- | --- |
| Map Table UI | `?ui-preview=table` | `-ui-preview table` | Restyles the HUD, panels and Cabinet (`UiPreview.cs`, `UiPreview.uss`) |
| Building prototypes | `?building-preview` | `-building-preview` | Replaces the kibbutz, Tel Aviv and arab-town markers with prototype models (`BuildingPreview.cs`) |

Neither switch changes the simulation: the golden and smoke hashes in `docs/determinism.md` are the same with and
without them. Editor Play mode receives no URL, so it always shows the shipped look.

## Local prototype models

1. Keep the original exports outside `unity/Assets/`. Each model is a folder `<name>/fbx/` holding one `.fbx` and its
   `.fbm/` texture folder; the known names are listed in `PrototypeImport.cs`.
2. Import: `"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod PrototypeImport.Run -prototypeSource <folder>`.
   It writes `unity/Assets/Art/Settlements/Prototype/` (git-ignored): the models, URP Lit materials and prefabs under
   that folder's own `Resources/`.
3. Check them without UI: `-executeMethod BuildingPreviewCapture.Run -captureDir <dir>` renders before/after pairs.

While that folder exists, every player build contains the models. Keep such a build local: `Build` refuses a
`-shareable` build, marks a WebGL build folder with `PENDING-RIGHTS-PROTOTYPES.txt`, and `tools/package_release.py`
refuses a marked folder. To admit a model later, record its source, licence and status in `ASSETS.yaml` and
`LICENSING.md`, move it out of the prototype folder, and make the placement a reviewed change.
