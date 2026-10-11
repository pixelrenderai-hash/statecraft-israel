# Look and play: a map pass, then two options to test (design, 12 October 2026)

Tal's goal: the game should look amazing and feel like StarCraft, with historical subject matter, for the least work.
Approved in conversation on 11 October 2026. Tal's standing rules (CLAUDE.md) still hold: everything is opt-in behind a
switch, the default game and the simulation goldens stay unchanged, Tal approves matched before/after captures at every
gate, and nothing is pushed, deployed or shared without an explicit instruction.

## Diagnosis (from the wave-3 build, `?building-preview&ui-preview=table`)

1. The land is empty: no trees, orchards, fields, roads or railway. Models are dots on flat tan terrain.
2. The image is washed out: heavy blur, flat colour, low contrast.
3. The top bar is text only: no icons, no change values, no motion. "Cabinet" overlaps "$40.0m" in the narrow pane
   (unchecked at 1280 and 1920 wide; check in Option A's HUD work).
4. The project has no audio at all.
5. End Turn has no drama: numbers change, the map does not react.

## Order and gates

Step 0 → Tal approves captures → Option A → Tal plays it → Option B → Tal plays both and chooses (A, B or a hybrid).
Each arrow is a gate. Only Step 0 is planned in detail now; A and B get their own plans when their gate opens.

## Step 0: the map looks amazing (shared by both options)

Switch: `?map-look` (web), `-map-look` (desktop), parsed like `BuildingPreview.Requested`. Without it the map is byte-for-byte
the current one. It composes with `?building-preview` and `?ui-preview=table`.

- **Scatter.** Instanced olive, citrus, cypress, palm, wheat-field patches, stone walls and rocks, chosen by the biome
  weights `BiomeBlender` already computes and by slope and elevation from the heightmap. Placement uses a fixed seed, so it
  is deterministic and capture-stable. Density follows `QualityTierValues` (fewer on the low tier). Nothing is placed in the
  sea, on steep slopes or inside a settlement footprint.
- **Towns read as towns.** Tel Aviv, Jaffa, Haifa and Jerusalem become clusters of several buildings: instanced repeats of
  existing prototypes and CC0 blocks, kept under the label anchor (`MarkerHeightM`).
- **Lines on the land.** The 1948 railway (Haifa–Lod–Jerusalem and the coast line) and main roads as hand-placed
  polylines with sources cited in the data file, drawn as thin terrain decals or meshes. Fishing boats and steamers off the
  coast reuse the wave-3 props.
- **Picture.** A `map-look` grade profile: less tilt-shift blur, warmer and more saturated colour, more contrast, a richer
  sea. It is applied only under the switch; the shipped `MapGrade` and `TiltShift` defaults and their tests are unchanged.
  Each piece registers a `VisualToggles` entry (`look-scatter`, `look-towns`, `look-lines`, `look-grade`) so the visual
  profile can price it.
- **Assets.** CC0 packs only (Quaternius, Kenney, KayKit, Poly Haven), committed with `ASSETS.yaml` rows (licence CC0-1.0,
  source URL, status verified) so they can ship publicly. No Unity Asset Store packs, because their licence does not allow
  redistribution in an open-source repository. No Tripo spend in Step 0.
- **Budgets** (`docs/visual-v1.md`). The WebGL build grows by at most 15 MB. Scatter, towns and lines together cost at most
  2.5 ms median at both promo poses (headless, 1920x1080). Boot spans stay within the existing budget.
- **Done when:** matched captures of the same deterministic turn and poses (Gameplay03, Diorama02, plus the Tel Aviv
  close-up from Tal's screenshot), switch on against switch off, are approved by Tal. EditMode stays green, goldens and
  smoke=5 pass in every switch combination, `check_repo` and policy tests pass.

## Option A: "Living War Room" (rules unchanged)

A turn-based campaign map in the style of Total War. Presentation only; reads simulation state, never writes it.

- **Visible units:** brigades from the reserve call-up and security level, immigrant ships arriving at Haifa, convoys to the
  Negev, trucks to transit camps, all derived from the state each turn.
- **Turn drama:** at End Turn the camera visits where things changed, units move, numbers float over settlements
  ("+320 immigrants"), the minimap pings, alerts sound. Skippable with one key.
- **1948 war-room HUD** with StarCraft's structure in period materials (dark steel, brass, radio static, typewriter):
  - a report window with an office portrait;
  - command buttons with hotkeys;
  - icons and change values in the top bar;
  - the overlap fixed.
- **Sound:** CC0 music, UI clicks, ambient sea and town sounds, and alert sounds.
- **Content policy** (`docs/CONTENT_POLICY.md` §2): no words attributed to a real person unless sourced verbatim.
  - Reports speak as offices ("Defence Ministry: reserves mobilised").
  - A real minister is quoted only from a sourced quote.

## Option B: "Command" (throwaway prototype)

StarCraft-lite, to test whether direct control is more fun. It runs in a separate opt-in mode (`?prototype=command`). Its
code is clearly marked prototype, is not wired into `sim/` and never ships. If Tal picks it, the rules are rebuilt properly
in the deterministic simulation as a separate project.

- Select and move brigades, convoys and Palmach scouts on the map.
- Gather resources:
  - citrus groves earn foreign currency;
  - immigrants become workers;
  - workers build camps and settlements on the map.
- Short real-time-with-pause sessions over the same region and period as Option A.

## How we choose

Tal plays each for about 10 minutes, over the same region (coastal plain and Negev, War of Independence) and the same
5 turns. Score:

- fun;
- whether a screenshot looks amazing;
- cost to make it real;
- disruption to collaborators.

A hybrid (A's map with a few commandable units from B) is a valid outcome.

## Out of scope

Rule changes in `sim/`, new content rows, deployment, Tripo batches. The Tripo ceiling is unresolved: memory records
5,000 with 4,620 used and Tal mentioned 7,000 left. It must be confirmed with Tal and the live balance checked before any
spend.
