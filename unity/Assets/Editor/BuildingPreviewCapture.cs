#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Statecraft.Game;
using Statecraft.Game.Map;
using Statecraft.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Editor renders of the opt-in building prototypes (BuildingPreview, PreviewProps, PrototypeImport).
/// Map mode (the default): for each pose, &lt;-captureDir&gt;/before-&lt;pose&gt;.png with the marker boxes and
/// after-&lt;pose&gt;.png with the prototypes and props from Resources, 1920x1080 at the current quality tier, the
/// CameraRig's pitch and FOV, no UI (editor renders show no UI Toolkit labels). A transit camp and a housing project on two
/// neighbouring hexes beside Rishon LeZion are drawn through the scene's PlacementLayer in both (pose "placements"), since
/// a game reaches them only in 1950. Opens and builds Map.unity as MapCapture does, without saving it; leaves the boxes
/// shown and the placements cleared again. Every prototype is optional: a mapping none of whose names loads is listed,
/// and the run fails (exit 1 in batch mode) only when nothing loads or nothing is placed.
/// Turntable mode (-captureTurntable): every prefab under Resources/SettlementPrototypes alone on a neutral ground,
/// &lt;-captureDir&gt;/turntable/&lt;name&gt;-&lt;yaw&gt;.png at 1024x1024 for yaw 0, 90, 180 and 270, framed to its bounds, for
/// silhouettes and topology. The model is turned by yaw and the camera looks north from the south as the map's does, so
/// the picture that shows the front gives the yaw for BuildingPreview's or PreviewProps' table directly.
/// A relative directory is taken from the repo root; the default, captures/building-preview, is git-ignored.
/// Unity -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -executeMethod BuildingPreviewCapture.Run [-captureDir &lt;dir&gt;] [-captureTurntable]</summary>
public static class BuildingPreviewCapture
{
    const string ScenePath = "Assets/Map.unity";

    /// <summary>Poses of its own, not PromoPose's lists (tests pin PromoPose.All and CloseChecks, and art_checkpoint wants a
    /// WebGL shot of every pose in PromoPose.cs). Gameplay03 is the art checkpoint's own frame. A pose looks at its point at
    /// y = 0, the heightmap's floor (PromoPose.Apply, as the game's camera does), 1.3 km below the sea and more below land,
    /// so at these zooms the frame's centre falls kilometres south of the point (at Jerusalem's own coordinates the city
    /// would sit above the top edge). The second and third batches' points are therefore set north of their subjects, worked
    /// out from the heightmap with the rig's pitch and FOV at 16:9; the comment gives what is in frame.</summary>
    public static IReadOnlyList<PromoPose> Poses { get; } = new[]
    {
        new PromoPose("tel_aviv", 34.78, 32.075, 9000f, 10f),          // the White City cluster close up
        new PromoPose("lod_ramla", 34.88, 31.94, 9000f, 10f),          // the stone-village stand-in on two arab_towns, the locomotive top left
        new PromoPose("degania", 35.5747, 32.7081, 6000f, 10f),        // a kibbutz at the 6 km hero zoom, the farmer south of it
        new PromoPose("jerusalem", 35.233, 31.813, 9000f, 10f),        // the stone city in the centre
        new PromoPose("haifa_port", 35.021, 32.846, 9000f, 10f),       // the steamer in the bay, the port city lower left
        new PromoPose("haifa_carmel", 35.004, 32.812, 6000f, 10f),     // the immigrant woman below the centre, the elder left, the boy bottom right
        new PromoPose("latrun", 35.001, 31.833, 9000f, 10f),           // the fort and the soldier left, the truck on the Bab al-Wad road right
        new PromoPose("lod_airport", 34.887, 32.017, 9000f, 10f),      // the C-54 overhead, the Yemenite immigrant right, the nurse cut off top left
        new PromoPose("tel_aviv_shore", 34.76, 32.083, 9000f, 10f),    // the Altalena off the beach, the White City right of it
        new PromoPose("nahalal", 35.199, 32.703, 6000f, 10f),          // the moshav in the centre, the horse wagon right
        new PromoPose("rishon_lezion", 34.803, 31.963, 7000f, 10f),    // a moshava in the centre, the builder in the top-left corner
        new PromoPose("carmel_family", 35.002, 32.806, 6000f, 10f),    // the immigrant woman centred, the boy right, the elder left
        new PromoPose("ein_harod", 35.395, 32.565, 7000f, 10f),        // the kibbutz above the centre, the field worker left, the tractor right
        new PromoPose("nazareth", 35.313, 32.698, 9000f, 10f),         // the villager below the centre, Nazareth at the top
        new PromoPose("hadera", 34.891, 32.438, 9000f, 10f),           // the fishing boat left, the bus right, the moshava at the top
        new PromoPose("tel_hashomer", 34.845, 32.059, 7000f, 10f),     // the nurse in the centre
        new PromoPose("lod_junction", 34.867, 31.968, 7000f, 10f),     // the locomotive left of the centre, Lod at the right edge
        new PromoPose("rehovot", 34.83, 31.911, 7000f, 10f),           // the moshava left, the citrus truck right
        new PromoPose("revivim", 34.723, 31.053, 9000f, 10f),          // the kibbutz above the centre, the jeep left, the scout lower right
        PromoPose.Gameplay03,
    };

    /// <summary>Distance of the "placements" pose: two neighbouring hex centres (8.7 km apart on the 5 km hexes) in frame.</summary>
    const float PlacementsDistanceM = 12000f;
    /// <summary>Axial neighbours in turn, so consecutive ones are neighbours of each other too.</summary>
    static readonly (int q, int r)[] Neighbours = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

    static readonly float[] TurntableYaws = { 0f, 90f, 180f, 270f };
    const int TurntableSize = 1024;
    const float TurntableFov = 30f, TurntablePitch = 25f, TurntableMargin = 1.1f;
    /// <summary>The ground disc spans this many bounding radii, so the model's shadow lands on it.</summary>
    const float GroundRadii = 6f;
    static readonly Color Backdrop = new Color(0.78f, 0.80f, 0.82f), GroundColour = new Color(0.55f, 0.55f, 0.53f), Ambient = new Color(0.45f, 0.45f, 0.47f);

    public static void Run()
    {
        try
        {
            var dir = Path.GetFullPath(Path.Combine(Paths.RepoRoot, Arg("-captureDir") ?? "captures/building-preview"));
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-captureTurntable") >= 0) Turntable(Path.Combine(dir, "turntable"));
            else Capture(dir);
        }
        catch (Exception e)
        {
            Debug.LogError("BUILDING PREVIEW CAPTURE FAILED " + e.Message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    static void Capture(string dir)
    {
        // A row is missing only when none of its names loads: a fallback or a density variant behind a loaded name is no gap.
        var rows = BuildingPreview.Rows();
        var missing = rows.Where(r => BuildingPreview.Resolve(r, BuildingPreview.LoadFromResources).name == null).Select(r => string.Join(" or ", r)).ToList();
        if (missing.Count == rows.Count)
            throw new FileNotFoundException($"no prototype under Resources/{BuildingPreview.ResourcesFolder} (run PrototypeImport first)");
        if (missing.Count > 0) Debug.LogWarning($"BUILDING PREVIEW CAPTURE not imported (each optional): {string.Join(", ", missing)}");
        // MapCapture.OpenAndBuild: open the scene (made only when missing), build in memory, never save.
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) MapCapture.MakeScene();
        else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var map = MapCapture.RequireMapScene(); map.Build();
        var cam = Camera.main;
        var rig = cam.GetComponent<CameraRig>();
        var content = ContentLoader.Load(Paths.Content);
        var layer = Object.FindFirstObjectByType<PlacementLayer>();
        if (layer == null) layer = new GameObject("Placements").AddComponent<PlacementLayer>();   // the scene is never saved
        var demo = DemoPlacements(content);
        var poses = Poses.Concat(demo != null ? new[] { demo.Value.pose } : Array.Empty<PromoPose>()).ToList();
        var placements = demo?.placements ?? new List<Placement>();
        try
        {
            BuildingPreview.Apply(map, false, BuildingPreview.LoadFromResources);
            layer.Render(placements, content, map);
            foreach (var pose in poses) Shoot(cam, rig, map, pose, Path.Combine(dir, $"before-{pose.Name}.png"));
            int placed = BuildingPreview.Apply(map, true, BuildingPreview.LoadFromResources);
            layer.Render(placements, content, map);   // drawn again now the preview is on: DressPlacement
            var props = map.transform.Find(PreviewProps.RootName);
            int propCount = props != null ? props.childCount : 0;
            int dressed = layer.transform.Cast<Transform>().Count(t => t.childCount > 0);   // a box has no child, a dressed holder one
            if (placed + propCount + dressed == 0) throw new InvalidOperationException("no prototype was placed: the after images would equal the before ones");
            foreach (var pose in poses) Shoot(cam, rig, map, pose, Path.Combine(dir, $"after-{pose.Name}.png"));
            Debug.Log($"BUILDING PREVIEW CAPTURE {placed} settlements, {dressed} placements, {propCount} props, {poses.Count} poses, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} -> {dir}");
        }
        finally
        {
            BuildingPreview.Apply(map, false, BuildingPreview.LoadFromResources);
            layer.Render(Array.Empty<Placement>(), content, map);
        }
    }

    /// <summary>A transit camp and a housing project on the first two neighbouring hexes beside Rishon LeZion that are land and
    /// own no settlement, and the pose between them; null when the content has no such pair.</summary>
    static (List<Placement> placements, PromoPose pose)? DemoPlacements(Content c)
    {
        var home = c.SettlementHexes.FirstOrDefault(s => s.Id == "rishon_lezion");
        if (home == null) return null;
        var owned = new HashSet<(int, int)>(c.SettlementHexes.Select(s => (s.Q, s.R)));
        MapHex? Free(int i)
        {
            var (dq, dr) = Neighbours[i % Neighbours.Length];
            var h = c.HexAt(home.Q + dq, home.R + dr);
            return h != null && h.Land && !owned.Contains((h.Q, h.R)) ? h : null;
        }
        for (int i = 0; i < Neighbours.Length; i++)
        {
            var (a, b) = (Free(i), Free(i + 1));
            if (a == null || b == null) continue;
            var date = new DateTime(1950, 8, 1);
            var list = new List<Placement> { new Placement(MapActions.TransitCamp, a.Q, a.R, date), new Placement(MapActions.Housing, b.Q, b.R, date) };
            return (list, new PromoPose("placements", (a.Lon + b.Lon) / 2, (a.Lat + b.Lat) / 2, PlacementsDistanceM, 10f));
        }
        return null;
    }

    /// <summary>Every prefab under Resources/SettlementPrototypes alone in a new, unsaved scene: a directional light, flat
    /// ambient, a matte ground under it, and a camera at TurntablePitch that fits the bounds at each yaw.</summary>
    static void Turntable(string dir)
    {
        var prefabs = Resources.LoadAll<GameObject>(BuildingPreview.ResourcesFolder).OrderBy(p => p.name, StringComparer.Ordinal).ToList();
        if (prefabs.Count == 0) throw new FileNotFoundException($"no prototype under Resources/{BuildingPreview.ResourcesFolder} (run PrototypeImport first)");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Ambient;
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);   // from behind the camera's right
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10 x 10 units
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("shader not found: Universal Render Pipeline/Lit");
        var groundMat = new Material(shader);
        groundMat.SetColor("_BaseColor", GroundColour); groundMat.SetFloat("_Smoothness", 0f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
        var cam = new GameObject("TurntableCamera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Backdrop; cam.fieldOfView = TurntableFov;
        int shots = 0;
        try
        {
            foreach (var prefab in prefabs)
            {
                var go = Object.Instantiate(prefab);
                foreach (var a in go.GetComponentsInChildren<Animator>(true)) PreviewProps.SampleIdle(a);
                foreach (var yaw in TurntableYaws)
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
                    var bounds = Measure(go);
                    if (bounds == null) { Debug.LogWarning($"BUILDING PREVIEW TURNTABLE {prefab.name} has nothing to draw"); break; }
                    var b = bounds.Value;
                    float radius = Mathf.Max(b.extents.magnitude, 1e-3f);
                    ground.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                    ground.transform.localScale = Vector3.one * (radius * GroundRadii / 10f);
                    float distance = radius / Mathf.Sin(0.5f * TurntableFov * Mathf.Deg2Rad) * TurntableMargin;
                    var look = Quaternion.Euler(TurntablePitch, 0f, 0f);   // yaw 0: looking north, as the map's camera does
                    cam.transform.SetPositionAndRotation(b.center - look * Vector3.forward * distance, look);
                    cam.nearClipPlane = distance / 50f; cam.farClipPlane = distance * 10f;
                    Save(cam, TurntableSize, TurntableSize, Path.Combine(dir, FormattableString.Invariant($"{prefab.name}-{yaw:0}.png")));
                    shots++;
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log($"BUILDING PREVIEW TURNTABLE {prefabs.Count} prefabs, {shots} images -> {dir}");
        }
        finally { Object.DestroyImmediate(groundMat); }
    }

    static Bounds? Measure(GameObject go)
    {
        Bounds? all = null;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (all is Bounds b) { b.Encapsulate(r.bounds); all = b; }
            else all = r.bounds;
        }
        return all;
    }

    static void Shoot(Camera cam, CameraRig rig, MapScene map, PromoPose pose, string path)
    {
        pose.Apply(rig, map.Frame);
        Save(cam, 1920, 1080, path);
    }

    /// <summary>MapCapture.Save: one camera render read back into a PNG.</summary>
    static void Save(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }

    static string? Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
