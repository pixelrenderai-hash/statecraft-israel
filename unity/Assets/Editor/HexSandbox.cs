#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Statecraft.Game;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Local look sandbox: dresses only a small patch of real hexes (a ring of radius -sandboxRadius, default 3 = 37
/// hexes) around each named settlement and renders it, so planting rules, vegetation models and town kits can be tried
/// without the game, the UI or the full map-look pass. Per patch and mode, &lt;dir&gt;/&lt;patch&gt;-&lt;mode&gt;-{wide,close}.png at
/// -captureSize (default 3840x2160) through the CameraRig's pitch and FOV, looking at the settlement on the ground, with MapLook's lighter
/// tilt-shift; and stats.txt (plants, triangles per land hex, the same density over all 870 land hexes, build ms).
/// Modes: "bare" (terrain only), "baseline" (MapLookScatter and MapLookTowns' own rules on the patch) and "regions"
/// (HexSandboxPlanting's north / centre / Negev species plus MapLookTowns) and "ground" (regions on HexSandboxGround's
/// textured terrain) and "hexes" (Civ-style tiles on that ground: one role per hex, cities grown over neighbouring hexes,
/// HexSandboxCity and HexSandboxTiles). Unless -sandboxNoPrototypes, the dressed modes
/// also show Tal's local Tripo prototypes (BuildingPreview: settlements, figures, vehicles), which stay local-only.
/// Opens and builds Map.unity as BuildingPreviewCapture does, never saves it; the default game and map-look are untouched.
/// Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod HexSandbox.Run
///   [-captureDir &lt;dir&gt;] [-sandboxRadius 3] [-sandboxPatches safed,ein_harod,tel_aviv,beersheba]
///   [-sandboxModes bare,baseline,regions,ground,hexes] [-sandboxNoPrototypes] [-captureSize 3840x2160]</summary>
public static class HexSandbox
{
    const string ScenePath = "Assets/Map.unity", RootName = "HexSandbox";
    static List<string> stats = new List<string>();
    /// <summary>Capture size, -captureSize WxH; 4K by default so details read when zoomed in on a screenshot.</summary>
    static int width = 3840, height = 2160;
    /// <summary>North to south: Upper Galilee hills, the Jezreel valley, the coastal plain, the northern Negev.</summary>
    static readonly string[] DefaultPatches = { "safed", "ein_harod", "tel_aviv", "beersheba" }, DefaultModes = { "baseline", "regions" };
    const float CloseDistanceM = 9000f, HerdDistanceM = 5000f, Yaw = 10f, MapLandHexes = 870f;
    /// <summary>Pointy-top hexes: centre-to-centre spacing is sqrt(3) x size.</summary>
    const float HexSpacingShare = 1.7320508f;

    [MenuItem("Statecraft/Hex Sandbox Capture")]
    public static void Run()
    {
        try
        {
            var dir = Path.GetFullPath(Path.Combine(Paths.RepoRoot, Arg("-captureDir") ?? "asset-staging/ui-local/hex-sandbox"));
            int radius = int.TryParse(Arg("-sandboxRadius"), out var r) ? Mathf.Clamp(r, 1, 6) : 3;
            var patches = (Arg("-sandboxPatches") ?? string.Join(",", DefaultPatches)).Split(',', StringSplitOptions.RemoveEmptyEntries);
            var modes = (Arg("-sandboxModes") ?? string.Join(",", DefaultModes)).Split(',', StringSplitOptions.RemoveEmptyEntries);
            bool prototypes = Array.IndexOf(Environment.GetCommandLineArgs(), "-sandboxNoPrototypes") < 0;
            var size = (Arg("-captureSize") ?? "3840x2160").Split('x');
            width = int.Parse(size[0]); height = int.Parse(size[1]);
            Capture(dir, radius, patches, modes, prototypes);
        }
        catch (Exception e)
        {
            Debug.LogError("HEX SANDBOX FAILED " + e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    static void Capture(string dir, int radius, string[] patches, string[] modes, bool prototypes)
    {
        var clock = Stopwatch.StartNew();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) MapCapture.MakeScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var map = MapCapture.RequireMapScene(); map.Build();
        long mapMs = clock.ElapsedMilliseconds;
        var cam = Camera.main;
        var rig = cam.GetComponent<CameraRig>();
        Directory.CreateDirectory(dir);
        stats = new List<string> { $"map build {mapMs} ms, radius {radius}, modes {string.Join(",", modes)}, prototypes {prototypes}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}" };
        float tilt = TiltShift.RadiusScale;
        try
        {
            TiltShift.RadiusScale = MapLook.TiltRadiusScale;
            foreach (var id in patches)
            {
                var home = map.Settlements.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"no settlement '{id}'");
                var patch = Patch(map.Hexes, home.Lon, home.Lat, radius);
                int land = patch.hexes.Count(h => h.land);
                var biomes = string.Join(" ", patch.hexes.Where(h => h.land).GroupBy(h => $"{HexSandboxPlanting.RegionOf(h.lat)}/{h.biome}").OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"));
                stats.Add($"{id}: {patch.hexes.Length} hexes, {land} land ({biomes})");
                float wide = (2 * radius + 1) * map.Hexes.hex_size_km * 1000f * HexSpacingShare * 0.95f;
                var at = map.Frame.ToWorld(home.Lon, home.Lat, 0f);
                at.y = map.Terrain.SampleHeight(at) + map.Terrain.transform.position.y;
                foreach (var mode in modes)
                {
                    clock.Restart();
                    var owned = new List<Object>();
                    // "ground": the regions planting on HexSandboxGround's textured terrain.
                    bool textured = mode == "ground" || mode == "hexes";
                    var root = Dress(map, patch, mode == "ground" ? "regions" : mode, owned, out int plants, out int animals);
                    var undoGround = textured ? HexSandboxGround.Apply(map) : null;
                    var undoGrade = textured ? Grade(rig) : null;   // MapLook's livelier grade, as the opt-in look has it
                    if (textured && undoGround == null) throw new FileNotFoundException("ground textures missing (see the warning above)");
                    int placed = prototypes && mode != "bare" ? BuildingPreview.Apply(map, true, BuildingPreview.LoadFromResources) : 0;
                    long dressMs = clock.ElapsedMilliseconds;
                    try
                    {
                        // "herd": the largest herd (the animal with most neighbours within 3 km) at the 6 km hero zoom.
                        var beasts = root.transform.Find("herds") is Transform hs ? hs.Cast<Transform>().Select(c => c.position).ToList() : new List<Vector3>();
                        var shots = new List<(string, Vector3, float)> { ("wide", at, wide), ("close", at, CloseDistanceM) };
                        if (beasts.Count > 0) shots.Add(("herd", beasts.OrderByDescending(b => beasts.Count(o => (o - b).sqrMagnitude < 9e6f)).First(), HerdDistanceM));
                        foreach (var (name, look, distance) in shots)
                        {
                            rig.LookAt = look; rig.Distance = distance; rig.Yaw = Yaw; rig.Apply();
                            Save(cam, Path.Combine(dir, $"{id}-{mode}-{name}.png"));
                        }
                        var filters = root.GetComponentsInChildren<MeshFilter>();
                        long tris = filters.Sum(f => (long)f.sharedMesh.triangles.Length / 3);
                        long animalTris = root.GetComponentsInChildren<SkinnedMeshRenderer>().Sum(r => (long)r.sharedMesh.triangles.Length / 3);
                        long perHex = land > 0 ? tris / land : 0;
                        stats.Add($"  {mode}: {plants} plants, {filters.Length} renderers, {tris} tris = {perHex}/land hex (x870 = {perHex * MapLandHexes / 1e6f:0.00} M), prototypes {placed}, {animals} animals ({animalTris / 1000} k tris), dress {dressMs} ms");
                    }
                    finally
                    {
                        if (placed > 0 || prototypes) BuildingPreview.Apply(map, false, BuildingPreview.LoadFromResources);
                        undoGround?.Invoke(); undoGrade?.Invoke();
                        Object.DestroyImmediate(root);   // MapLookAssets releases its own; the planting's are in owned
                        foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
                    }
                }
            }
        }
        finally { TiltShift.RadiusScale = tilt; }
        File.WriteAllLines(Path.Combine(dir, "stats.txt"), stats);
        Debug.Log("HEX SANDBOX " + string.Join(" | ", stats) + " -> " + dir);
    }

    /// <summary>The hexes within <paramref name="radius"/> axial steps of the hex nearest (lon, lat).</summary>
    public static HexFile Patch(HexFile all, double lon, double lat, int radius)
    {
        var centre = all.hexes.OrderBy(h => (h.lon - lon) * (h.lon - lon) + (h.lat - lat) * (h.lat - lat)).First();
        int Distance(HexJson h) { int dq = h.q - centre.q, dr = h.r - centre.r; return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2; }
        return new HexFile { bounds = all.bounds, hex_size_km = all.hex_size_km, layout = all.layout, hexes = all.hexes.Where(h => Distance(h) <= radius).ToArray() };
    }

    static GameObject Dress(MapScene map, HexFile patch, string mode, List<Object> owned, out int plants, out int animals)
    {
        var root = new GameObject(RootName);
        root.transform.SetParent(map.transform, false);
        plants = 0; animals = 0;
        if (mode == "bare") return root;
        var assets = root.AddComponent<MapLookAssets>();
        assets.Init(map.MarkerMaterial);
        var t = map.Terrain; var data = t.terrainData; var origin = t.transform.position;
        float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
        float Slope(Vector3 p) => data.GetSteepness((p.x - origin.x) / data.size.x, (p.z - origin.z) / data.size.z);
        using var surface = new MapLookSurface(map);
        var avoid = MapLookScatter.Avoid(map);
        if (mode == "baseline")
        {
            var planted = MapLookScatter.Plan(patch, map.Frame, Ground, Slope, PreviewProps.SeaLevelY(), avoid, 1f, surface.IsWater);
            bool IsTree(string s) => s == MapLookShapes.Olive || s == MapLookShapes.Citrus || s == MapLookShapes.Cypress || s == MapLookShapes.Palm;
            assets.Emit(root.transform, "trees", planted.Where(p => IsTree(p.Shape)).Select(p => (p.Shape, p.Matrix)), shadows: true);
            assets.Emit(root.transform, "ground", planted.Where(p => !IsTree(p.Shape)).Select(p => (p.Shape, p.Matrix)), shadows: false);
            plants = planted.Count;
        }
        else if (mode == "regions")
        {
            var planted = HexSandboxPlanting.Plan(patch, map.Frame, Ground, Slope, avoid, surface.IsWater);
            HexSandboxPlanting.Emit(root.transform, planted, map.MarkerMaterial, owned);
            plants = planted.Count;
            var herds = new GameObject("herds").transform; herds.SetParent(root.transform, false);
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            animals = HexSandboxPlanting.PlaceHerds(herds, HexSandboxPlanting.PlanHerds(patch, map.Frame, Ground, Slope, avoid, surface.IsWater), missing);
            if (missing.Count > 0) Debug.LogWarning("HEX SANDBOX animals not imported: " + string.Join(", ", missing));
        }
        else if (mode == "hexes")
        {
            // Civ-style tiles: one role per hex (HexSandboxHex), cities by HexSandboxCity, nature by HexSandboxTiles.
            var tiles = HexSandboxHex.Roles(map.Hexes, patch, map.Settlements);
            float hexM = patch.hex_size_km * 1000f;
            var planted = new List<HexSandboxPlanting.Plant>();
            foreach (var tile in tiles.Values.OrderBy(t => t.Hex.q).ThenBy(t => t.Hex.r))
                planted.AddRange(tile.Role is HexSandboxHex.Role.CityCore or HexSandboxHex.Role.Quarter or HexSandboxHex.Role.Village
                    ? HexSandboxCity.Plan(tile, map.Frame, hexM, Ground, Slope, surface.IsWater)
                    : HexSandboxTiles.Plan(tile, map.Frame, hexM, Ground, Slope, avoid, surface.IsWater));
            HexSandboxPlanting.Emit(root.transform, planted, map.MarkerMaterial, owned);
            plants = planted.Count;
            HexSandboxEdges.Build(root.transform, tiles, map.Frame, hexM, Ground, surface.IsWater, owned);
            var wild = new HexFile { bounds = patch.bounds, hex_size_km = patch.hex_size_km, layout = patch.layout,
                hexes = tiles.Values.Where(t => t.Role is HexSandboxHex.Role.Farmland or HexSandboxHex.Role.Scrub or HexSandboxHex.Role.Desert or HexSandboxHex.Role.Orchard).Select(t => t.Hex).ToArray() };
            var herds = new GameObject("herds").transform; herds.SetParent(root.transform, false);
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            animals = HexSandboxPlanting.PlaceHerds(herds, HexSandboxPlanting.PlanHerds(wild, map.Frame, Ground, Slope, avoid, surface.IsWater), missing);
            stats.Add("  roles: " + string.Join(" ", tiles.Values.GroupBy(t => t.Role).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
            return root;   // cities replace MapLookTowns' blocks
        }
        else throw new ArgumentException($"unknown mode '{mode}' (bare, baseline, regions, ground, hexes)");
        var cells = new HashSet<(int, int)>(patch.hexes.Select(h => (h.q, h.r)));
        var inPatch = map.Settlements.Where(s => !double.IsNaN(s.Lon) && !double.IsNaN(s.Lat))
            .Where(s => { var h = Patch(map.Hexes, s.Lon, s.Lat, 0).hexes[0]; return cells.Contains((h.q, h.r)); });
        var blocks = MapLookTowns.Plan(inPatch, (lon, lat) => map.Frame.ToWorld(lon, lat, 0f), Ground, Slope, PreviewProps.SeaLevelY(), surface.IsWater);
        assets.Emit(root.transform, "towns", blocks.Select(b => (b.Shape, b.Matrix)), shadows: true);
        return root;
    }

    /// <summary>MapLook's picture values (its Picture class is private) on the rig's profile; returns the undo.</summary>
    static Action? Grade(CameraRig rig)
    {
        var profile = rig.Volume != null ? rig.Volume.profile : null;
        if (profile == null || !profile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca)) return null;
        var old = (ca.saturation.value, ca.saturation.overrideState, ca.contrast.value, ca.contrast.overrideState, ca.postExposure.value, ca.postExposure.overrideState);
        ca.saturation.Override(MapLook.Saturation); ca.contrast.Override(MapLook.Contrast); ca.postExposure.Override(MapLook.PostExposure);
        return () =>
        {
            ca.saturation.value = old.Item1; ca.saturation.overrideState = old.Item2; ca.contrast.value = old.Item3;
            ca.contrast.overrideState = old.Item4; ca.postExposure.value = old.Item5; ca.postExposure.overrideState = old.Item6;
        };
    }

    static void Save(Camera cam, string path)
    {
        var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }

    static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
