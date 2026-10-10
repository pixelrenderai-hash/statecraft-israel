# M3 release: the browser build

**Status:** M3 ships as the local browser build. The itch.io upload and the cross-upload save test are deferred to the
start of M4, as a private Draft (owner ruling, 2026-10-10); their sections below are kept, marked "Deferred to M4".

## What ships
- **Folder and zip:** `unity/Builds/Web-brotli/` uses Brotli compression with no decompression fallback. It is zipped as `unity/Builds/statecraft-m3-web.zip`, with `index.html` at the zip root. Both are build output and are not committed.
- **Built by:** `"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -buildTarget WebGL -executeMethod Statecraft.Game.EditorTools.Build.Web -compression brotli -shareable -logFile "$PWD/unity-build-web-br.log"` (as in `RELEASING.md`; `-shareable` also refuses while the local pending-rights prototype models are in the project), then `python3 tools/package_release.py` (it zips the folder, `index.html` at the root, and refuses until the sensitivity review below is done, or when the build is marked as made with the prototypes). Never zip by hand.
- **After building:** drop the build's rewrites of the settings with
  `git checkout -- unity/Assets/Settings unity/ProjectSettings/ProjectSettings.asset` (as in `dev-setup.md`), so no
  build-cache change gets committed.
- **Verified locally by:** `uv run tools/web_check.py unity/Builds/Web-brotli --query "golden&smoke=5"` with the three
  full `--expect` values pinned in `docs/determinism.md` (both goldens and the smoke line). This runs both golden hashes
  in the WebGL IL2CPP player, then plays five turns through the UI's own buttons (answering two events through the card:
  the IDF founding order on turn 2, the first truce on turn 4).
  - **Result at the merge candidate (2026-10-11, commit a099655, Brotli build):** `GOLDEN OK 1950 71e9904d…`,
    `GOLDEN OK 1952 73590e32…`, `SMOKE OK 5 turns 1948-06-19 d3fdd459…` (all three matched in full), then PASS.
  - History: 2026-10-10 at d450566 (Brotli, before the 1948 war-period events): `GOLDEN OK 1950 ee5d0826…`,
    `GOLDEN OK 1952 91bb6fb5…`, `SMOKE OK 5 turns 1948-06-19 7004c29a…`, PASS; folder 23 MB in 36 files, zip 20,862,385
    bytes. After the 1948 events (playtest 1 E, Disabled build): the current hashes, PASS.
- **Size (merge candidate, a099655):**
  - The folder is 23 MB (`du -sh`) in 51 files. itch.io allows 1,000 files.
  - The zip is 20 MB (20,941,435 bytes).
  - `StreamingAssets/map/heightmap.r16` (8 MB) is served as is, outside Unity's compression. Making it smaller is deferred to M5.
- **Play it locally:** serve the folder with web_check's handler, which sends `Content-Encoding: br` for `*.br`. Plain `python3 -m http.server` does not, so the Brotli build would not load. Then open http://localhost:8000/.
  ```
  python3 -c "import sys,functools,http.server; sys.path.insert(0,'tools'); import web_check as w; http.server.ThreadingHTTPServer(('127.0.0.1',8000), functools.partial(w.Handler, directory='unity/Builds/Web-brotli')).serve_forever()"
  ```
  The uncompressed build also works with `python3 -m http.server 8000 --directory unity/Builds/Web-disabled`.
- **Console lines a browser check can rely on:**
  - `GAME OK <date> turn <n> hash <state hash>` at start (a new game, or the autosave continued).
  - `TURN <n> <date> hash <state hash>` after every End Turn.
  - `CAMERA distance=<m> yaw=<deg>` with `?camlog`.
  - `PROFILE ...` with `?profile`.
  - `BOOT phase=<name> at=<ms> ms dt=<ms> ms` for each boot phase with `?bootlog` (playtest 2, P2-1).
  - `NAV f=<frame> ...` with `?navlog`: every raw mouse event, UI pointer and submit event, screen toggle and map click,
    with its frame (playtest 2, P2-4).
- **Player-facing wording:** the Budget screen gives each ministry's multiplier as "spending effect 150%" (the share's
  effect on that ministry's effects, on the 0-100 scale the rest of the game uses), never "x1.50" (playtest 2, P2-6).

## WebGL terrain profile
Results from `?profile`: 300 frames per setting after 60 warm-up frames, from the diorama camera over central Israel. All runs were in Chrome for Testing 1228 with `--disable-frame-rate-limit --disable-gpu-vsync`.

| `heightmapPixelError` | headless (`web_check.py`) avg / p95 ms | headed, Metal (Apple M5 Pro, 1728x858) avg / p95 ms |
|---|---|---|
| 1 (shipped; keeps the hex overlay draped) | 4.92 / 7.00 | 5.38 / 7.00 |
| 5 (Unity's default) | 1.51 / 2.00 | 2.19 / 4.00 |

**Decision: keep 1.** The p95 is 7 ms, far under the 33 ms of 30 fps (M2 ruling K2: above 1, the hex overlay sinks into valleys).

## Checks in a real browser (headed Chrome for Testing, ANGLE Metal), 2026-10-10
These were run with a small Chrome DevTools Protocol driver: `uv run --with websockets==15.0.1`, with a temporary profile and a fixed origin, `http://localhost:8765`.

### Rendering
- The terrain, hex overlay, settlement markers and labels, all UI Toolkit panels and the minimap render. Nothing is magenta.
- WebGL reports `ANGLE (Apple, ANGLE Metal Renderer: Apple M5 Pro)`.
- Three hidden URP utility shaders (`Hidden/CoreSRP/CoreCopy`, `Hidden/Universal Render Pipeline/StencilDitherMaskSeed`, `Hidden/Universal/HDRDebugView`) need shader target 4.5, which WebGL 2 (ES 3.0) lacks, so the browser reported them "not supported on this GPU". URP loads them up front (Blitter, UniversalRenderer, DebugHandler), but this build never draws with them: the render-graph copy needs framebuffer-fetch emulation, which Core RP disables on WebGL; stencil LOD cross-fade is off (Blue Noise, no LOD groups); the HDR debug view belongs to the Rendering Debugger. `Assets/Editor/WebShaderStripping.cs` strips them from WebGL builds only (playtest 1, item 12). The console no longer shows the three errors, and the goldens and the 5-turn smoke give the same hashes as before.
- The only 404 is `/favicon.ico`.

### Wheel zoom
- One wheel notch (Chrome sends 100 px) zooms by 10%: 120000 m to 108000 m to 97200 m.
- A 300 px burst zooms by three steps.
- The wheel over a panel does not zoom.
- Before this release a notch moved the camera only 0.08%. The Input System's default scroll behaviour already normalises a notch to 1, and the controller divided by 120 again; that division is now removed.

### Saves
Each step below passed:

| Step | Result |
|---|---|
| (a) Two turns, then a hard reload | Continued on the same turn and hash (turn 2, 1948-05-29). |
| (b) Tab closed immediately after End Turn's `TURN` line, then a new tab | Continued on turn 3. |
| (b) Whole browser quit immediately after a turn, then relaunched on the same profile | Continued on turn 4. |
| (c, local) Same origin, a second upload directory (`/upload-b/`, as an itch.io re-upload does) | Continued on turn 4. |
| (d) IndexedDB | Database `/idbfs`, store `FILE_DATA`, keys `/idbfs/statecraft-saves` and `/idbfs/statecraft-saves/autosave.txt`. No `JS_FileSystem_Sync` deprecation message. |
| (e) A private browser context (`Target.createBrowserContext`) | The game starts (a new game) and plays a turn. A second private context starts a new game again; no persistence is expected there. |

Firefox is not installed on the development Mac, so the Firefox check is open.

## OWNER STEP: sensitivity review before any shared build
Spec section 5.5 requires a recorded sensitivity review for events with sensitive tags (holocaust, terror, massacre,
war-casualties). The seven events below are only `fact-checked`. The owner reviews each and records the sign-off **only**
by moving the event's `status:` in its YAML file to `sensitivity-reviewed` and committing it (the loader accepts that
value). Nothing else counts as a sign-off.

- **Gate:** `Build.Web ... -shareable` and `Build.Mac ... -shareable` refuse to build while any sensitive event is not
  `sensitivity-reviewed`, and name them (`Statecraft.Sim.ReleaseGate.SensitiveUnreviewed`). `tools/package_release.py`
  makes the shareable zip and runs the same check (`Statecraft.Cli --release-gate`) on the content inside the build, so
  it prints `PACKAGE REFUSED ...` and exits 1 until the review is done. Use `-shareable` and the script for every build
  that leaves this machine, including the M4 private Draft once it is shared with anyone. Local dev builds (no flag)
  are not gated, because web_check and the golden checks need them.
- **Pinned set:** `FinalWaveTests.TheSensitiveEventsArePinned` lists the seven ids, and
  `TheOwnerTableListsEverySensitiveEvent` fails if a sensitive event is missing from this table, so a new one cannot
  skip the review.

| Event | Tags | What to review |
|---|---|---|
| `altalena` | security, war-casualties | Jews killed by Jews: 16 Irgun and 3 IDF dead (in the YAML comment, not the card) |
| `ten_days` | security, war-casualties | Lydda and Ramle are shown as a military operation only (Lydda airport, the Jerusalem road); leaving out the population's flight and expulsion is an editorial choice the notes flag for explicit approval |
| `operation_yoav` | security, war-casualties | the Hebron-hills decision; military text only |
| `operation_hiram` | security, war-casualties | military text only; the contested population figures and the 26 Sep cabinet quote are left out |
| `operation_horev` | security, war-casualties | military text only |
| `bernadotte_assassination` | security, terror | the `terror` tag follows the Israeli government's own Prevention of Terrorism Ordinance |
| `reparations_claim` | holocaust | the claim's framing (the cost of absorbing survivors: 500,000 at $3,000 each); the "refuse" option |

## Deferred to M4: upload to itch.io (OWNER STEP)
Deferred to the start of M4, as a private Draft (owner ruling, 2026-10-10). The steps stay correct for then. This needs the
owner's itch.io account and is never automated, and the build must pass the sensitivity gate above (`-shareable`).
1. Sign in at https://itch.io. Go to Dashboard, then **Create new project**.
2. Set the title to "Statecraft: Israel (M3 preview)", Kind of project to **HTML**, and Classification to Games.
3. Under Uploads, upload the zip made by `bash scripts/oss/package_web.sh unity/Builds/Web-brotli unity/Builds/statecraft-m3-web.zip` (it puts the licence and attribution files inside, then runs `tools/package_release.py`; see `docs/RELEASING.md`) and tick **This file will be played in the browser**.
4. Embed options:
   - Viewport **1280 × 720**. It fits a 1366×768 or 1440×900 laptop page without scrolling, and under the UI's
     ScaleWithScreenSize (reference 1920×1080) it lays out exactly like 1920×1080; `UiLayoutTests` covers 1280×720.
   - Tick **Fullscreen button**. ("Click to launch in fullscreen" is the alternative launch mode if the embed feels small.)
   - Leave **SharedArrayBuffer support** off; the build uses no threads.
5. Visibility & access:
   - Choose **Draft** first.
   - Open the page and play a turn. The browser console (DevTools) must show `GAME OK` and then `TURN 1`.
   - Then choose **Restricted** (password) or **Public**.
6. Record the page URL below under "Published at".

itch.io serves `.br` files with `Content-Encoding: br`, because it detects the extension, so no server settings are needed.

Published at: (owner fills in)

## Saves on itch.io: the fix, and its test (the test is deferred to M4)
- **Problem:** on WebGL, `Application.persistentDataPath` is `/idbfs/<MD5 of the page's directory URL>`. itch.io serves every upload from a new URL, so a new upload would hide the saves made with the previous one.
- **Fix (Task 21):** the autosave lives at the fixed path `/idbfs/statecraft-saves/autosave.txt`.
  - The template sets `autoSyncPersistentDataPath: true`, so closing the file persists it to IndexedDB.
  - IndexedDB is per origin, so saves survive re-uploads to the same project and stay separate per browser profile.
  - The local checks above passed: reload, tab close, browser restart, and a second directory on the same origin.
- **Test plan (OWNER STEP, deferred to M4 with the upload; after the first two uploads):**
  1. Upload A as above. Play two turns (press End Turn twice) and note the date and turn in the top bar.
  2. Reload the page. Expected: the game continues on that date. The console line `GAME OK <date> turn <n> hash ...` matches the last `TURN` line.
  3. Upload build B to the same project, replacing A. Rebuild first if anything changed.
  4. Open the page again with a hard reload. Expected: the game continues on the same date and turn.
     - If the content changed between A and B, a message reads "This save was made with different game content; the replayed game may differ."
     - **Pass** if the date and turn match.
  5. If the game starts again on 15 May 1948, the fixed folder did not persist across uploads. Record the browser and the page URL here, and use the deferred fallback (a localStorage bridge, or save export and import).
  6. Record the result here: date, browser, pass or fail.

Result: (owner fills in)

## Known limits of this build
- Placeholder settlement markers; per-era art is M4.
- No Diplomacy screen or legacy report (M4).
- English only.
- The headless smoke plays five turns; a full-slice smoke is M4.
- The wheel-zoom step (one notch = 10%) was measured in Chrome on macOS only. Firefox reports wheel deltas in lines and
  Windows at 125% scaling reports 125 px per notch, so re-check the step in the owner's M4 itch.io check.
- Not uploaded to itch.io: the upload and the cross-upload save test are deferred to the start of M4 (owner ruling,
  2026-10-10). The seven sensitive events await the owner's sensitivity review (see above).
