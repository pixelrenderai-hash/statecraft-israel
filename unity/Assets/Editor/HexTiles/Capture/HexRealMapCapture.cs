#nullable enable
using System;
using System.IO;
using System.Linq;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Statecraft.HexTiles.Capture
{
    /// <summary>The hex board on the game's real map (Map.unity, built as the game builds it, never saved): the board's
    /// hexes are the game's own q/r, so its root sits at (0, sea level, map length) with no turn or scale. Under the
    /// board the real ground is eased down to the board's own hex-shaped ground (blending out over about one hex), the
    /// board's terrain is cut away everywhere but its land hexes so the real coast, sea and country show around it, and
    /// the real settlement markers inside the board are hidden (the tiles replace them). Editor proof of the in-game
    /// layer: shots country.png (the board inside Israel), area.png, and per tile &lt;id&gt;-real.png.
    /// Unity -batchmode ... -executeMethod Statecraft.HexTiles.Capture.HexRealMapCapture.Run [-board tel_aviv]</summary>
    public static class HexRealMapCapture
    {
        const string ScenePath = "Assets/Map.unity";
        const float SinkM = 30f, BlendFromShare = 0.9f, BlendToShare = 1.7f;

        [MenuItem("Statecraft/Hex Tiles/Capture Board On Real Map")]
        public static void Run()
        {
            try
            {
                var repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                var dir = Path.GetFullPath(Path.Combine(repo, HexBoardCapture.Arg("-captureDir") ?? "asset-staging/ui-local/hex-tiles/real-map"));
                var size = (HexBoardCapture.Arg("-captureSize") ?? "3840x2160").Split('x');
                Capture(HexBoardCapture.Arg("-board") ?? "tel_aviv", dir, int.Parse(size[0]), int.Parse(size[1]));
            }
            catch (Exception e)
            {
                Debug.LogError("HEX REAL MAP FAILED " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Capture(string boardId, string dir, int width, int height)
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var map = Object.FindFirstObjectByType<MapScene>() ?? throw new InvalidOperationException("Map.unity has no MapScene");
            map.Build();
            var board = HexBoardCapture.LoadBoard(boardId);
            float sea = PreviewProps.SeaLevelY();
            var origin = new Vector3(0f, sea, map.Frame.LengthM);
            var surface = new HexBoardSurface(board);
            var centres = board.Hexes.Values.Select(h => HexGeometry.Centre(h.q, h.r, HexScale.HexM)).ToList();
            float Near(Vector3 b) => centres.Min(c => new Vector2(b.x - c.x, b.z - c.z).magnitude);

            // 1. Ease the real ground down to the board's ground under and around the board.
            var t = map.Terrain; var data = t.terrainData; var tp = t.transform.position;
            int res = data.heightmapResolution;
            var rect = HexBoardBuilder.Bounds(board);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((origin.x + rect.xMin - tp.x) / data.size.x * (res - 1)), 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((origin.x + rect.xMax - tp.x) / data.size.x * (res - 1)), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((origin.z + rect.yMin - tp.z) / data.size.z * (res - 1)), 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((origin.z + rect.yMax - tp.z) / data.size.z * (res - 1)), 0, res - 1);
            var heights = data.GetHeights(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
            for (int z = 0; z < heights.GetLength(0); z++)
                for (int x = 0; x < heights.GetLength(1); x++)
                {
                    var world = new Vector3(tp.x + data.size.x * (x0 + x) / (res - 1f), 0f, tp.z + data.size.z * (z0 + z) / (res - 1f));
                    var b = world - origin;
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BlendToShare * HexScale.HexM, BlendFromShare * HexScale.HexM, Near(b)));
                    if (w <= 0f) continue;
                    float hb = surface.Height(b);
                    if (hb < 0f) continue;   // the board's water: keep the real sea and seabed (its depth shading) untouched
                    float target = (sea + hb - SinkM - tp.y) / data.size.y;
                    heights[z, x] = Mathf.Lerp(heights[z, x], Mathf.Clamp01(target), w);
                }
            data.SetHeights(x0, z0, heights);

            // 2. Hide the real settlement markers that fall on the board's land (its tiles stand there instead).
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<Terrain>() != null || r.gameObject.name.IndexOf("marker", StringComparison.OrdinalIgnoreCase) < 0 && r.transform.parent?.name != "Settlements") continue;
                var b = r.bounds.center - origin;
                var (q, rr) = HexGeometry.Axial(b, HexScale.HexM);
                if (board.Hexes.TryGetValue((q, rr), out var h) && HexBoard.TerrainOf(h) != HexTerrain.Water) r.enabled = false;
            }

            // 3. The board on top: no sea of its own, terrain holes outside its land.
            var (root, reports) = HexBoardBuilder.Build(board, HexBoardCapture.Ground, options: new HexBoardBuilder.Options { OwnSea = false, HolesOutsideLand = true, Origin = origin });
            Directory.CreateDirectory(dir);
            var cam = Camera.main ?? throw new InvalidOperationException("no main camera");
            float tilt = TiltShift.RadiusScale;
            try
            {
                TiltShift.RadiusScale = MapLook.TiltRadiusScale;
                Vector3 World(Vector3 b) => origin + new Vector3(b.x, Mathf.Max(surface.Height(b), 0f), b.z);
                var middle = new Vector3(centres.Average(c => c.x), 0f, centres.Average(c => c.z));
                HexBoardCapture.Shoot(cam, World(middle), 160000f, Path.Combine(dir, "country.png"), width, height);
                HexBoardCapture.Shoot(cam, World(middle), 80000f, Path.Combine(dir, "area.png"), width, height);
                foreach (var h in board.Hexes.Values.Where(h => h.prototype is "tel_aviv_city" or "moshava_village" or "arab_village" || h.unit != ""))
                    HexBoardCapture.Shoot(cam, World(HexGeometry.Centre(h.q, h.r, HexScale.HexM)), 20000f, Path.Combine(dir, $"{(h.unit != "" ? "unit-" + h.unit : h.prototype)}-real.png"), width, height);
                Debug.Log($"HEX REAL MAP board {boardId} at {origin}, {reports.Count} tiles and units -> {dir}");
            }
            finally { TiltShift.RadiusScale = tilt; Object.DestroyImmediate(root); }
        }
    }
}
