# Map Look (Step 0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An opt-in `?map-look` switch (desktop `-map-look`) that fills the real map with orchards, olive groves, cypress,
palms, fields, rocks, town blocks and the 1948 railway, and gives it a livelier grade with lighter blur. Without the switch
the game is unchanged.

**Architecture:** One orchestrator (`MapLook`) owns a `MapLook` root under the map, mirroring `BuildingPreview`'s switch
pattern. All geometry is faceted low-poly meshes built in code (`MapLookShapes`), so no asset files, downloads or licences
are involved. Placement is planned by pure, testable functions (`MapLookScatter.Plan`, `MapLookTowns.Plan`,
`MapLookLines.Ribbon`), seeded and deterministic. Placed shapes are combined into one renderer per 20 km chunk and colour
(`MapLookAssets.Emit`). Each layer registers a `VisualToggles` switch so the visual profile prices it.

**Tech Stack:**
- Unity 6000.3.25f1 (pinned; never 6000.5.3f1), URP, C# with `#nullable enable`;
- NUnit EditMode tests;
- the WebGL build and the `tools/web_check.py` golden check.

**Spec:** `docs/superpowers/specs/2026-10-12-look-and-play-two-options-design.md` (Step 0).

**Ground rules (CLAUDE.md):**
- **Footprint:** the original game changes by exactly three lines: two in `TiltShift.cs`, one in `GameRoot.cs`.
- **Settings rewrites:** after any build or editor run, revert them before tests and commits:
  `git checkout -- unity/Assets/Settings unity/ProjectSettings/ProjectSettings.asset unity/ProjectSettings/QualitySettings.asset`.
- **Pending-rights models:** the build is local only while the Tripo models exist (`unity/Builds/`, marked). Never use
  `-shareable`, push or deploy.
- **Commits:** local only.
- **Generated meta files:** Unity writes a `.meta` file for each new `.cs` file. Commit it with its file.

**Commands used throughout:**

```bash
export UNITY="/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity"
```

EditMode, one fixture (fast):

```bash
"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -projectPath unity -buildTarget OSXUniversal -runTests -testPlatform EditMode -testFilter MapLookTests -testResults "$PWD/unity-editmode.xml" -logFile unity-tests.log; grep -o 'result="[A-Za-z]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' unity-editmode.xml | head -1
```

---

## File structure

| File | Responsibility |
| --- | --- |
| Create `unity/Assets/Scripts/Map/MapLook.cs` | Flag parsing, Apply/remove, picture (grade + tilt-shift), toggles |
| Create `unity/Assets/Scripts/Map/MapLookShapes.cs` | Low-poly meshes in code (trees, field, rock, block) |
| Create `unity/Assets/Scripts/Map/MapLookAssets.cs` | Palette, material and mesh ownership, chunked combine (`Emit`) |
| Create `unity/Assets/Scripts/Map/MapLookScatter.cs` | Biome rules and deterministic placement of vegetation, fields, rocks |
| Create `unity/Assets/Scripts/Map/MapLookTowns.cs` | Town block clusters around cities, towns and arab towns |
| Create `unity/Assets/Scripts/Map/MapLookLines.cs` | 1948 railway and main-road routes, draped ribbons |
| Create `unity/Assets/Tests/EditMode/MapLookTests.cs` | All tests for the above |
| Modify `unity/Assets/Scripts/Map/TiltShift.cs` | `RadiusScale` (default 1), used in `Params`, reset in `Reset` |
| Modify `unity/Assets/Scripts/UI/GameRoot.cs:132` | One line: `MapLook.Apply(map, MapLook.Requested());` |
| Modify `docs/local-ui-preview.md` | Step 0 section: switch, footprint, verification |

---

### Task 1: The switch and the picture

**Files:**
- Create: `unity/Assets/Scripts/Map/MapLook.cs`
- Modify: `unity/Assets/Scripts/Map/TiltShift.cs`
- Test: `unity/Assets/Tests/EditMode/MapLookTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `unity/Assets/Tests/EditMode/MapLookTests.cs`:

```csharp
#nullable enable
using System.Linq;
using NUnit.Framework;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>The opt-in map look (MapLook, spec 2026-10-12 Step 0): a strict flag, a picture that is restored exactly, and
/// layers that exist only while the switch is on. Fresh empty scene per test, as BuildingPreviewTests.</summary>
public class MapLookTests
{
    const string TerrainMatPath = "Assets/Materials/Terrain.mat", MarkerMatPath = "Assets/Materials/SettlementMarker.mat",
        SelectionMatPath = "Assets/Materials/HexSelection.mat", SeaMatPath = "Assets/Materials/Sea.mat", BorderLandMatPath = "Assets/Materials/BorderLand.mat";

    [SetUp] public void FreshScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TearDown]
    public void DiscardScene()
    {
        var map = Object.FindFirstObjectByType<MapScene>();
        if (map != null) MapLook.Apply(map, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    static Material Load(string path) { var m = AssetDatabase.LoadAssetAtPath<Material>(path); Assert.IsNotNull(m, path); return m; }

    static MapScene BuiltMap()
    {
        new GameObject("TestMainCamera") { tag = "MainCamera" }.AddComponent<Camera>();
        var map = new GameObject("MapScene").AddComponent<MapScene>();
        map.Configure(Load(TerrainMatPath), Load(MarkerMatPath), Load(SelectionMatPath), Load(SeaMatPath), Load(BorderLandMatPath));
        map.Build();
        return map;
    }

    static ColorAdjustments Grade()
    {
        var rig = Camera.main!.GetComponent<CameraRig>();
        Assert.IsTrue(rig.Volume.profile.TryGet(out ColorAdjustments ca));
        return ca;
    }

    [TestCase("http://localhost/?map-look", true)]
    [TestCase("http://localhost/?map-look=1", true)]
    [TestCase("http://localhost/?building-preview&map-look", true)]
    [TestCase("http://localhost/?map-looks", false)]
    [TestCase("http://localhost/?map-look=0", false)]
    [TestCase("http://localhost/#map-look", false)]
    [TestCase("http://localhost/?a=b#map-look", false)]
    [TestCase("http://localhost/?next=/p?map-look", false)]
    [TestCase(null, false)]
    public void TheWebFlagIsStrict(string? url, bool expected) =>
        Assert.AreEqual(expected, MapLook.Requested(url, new string[0], webgl: true));

    [Test]
    public void TheDesktopFlagIsTheExactArgument()
    {
        Assert.IsTrue(MapLook.Requested(null, new[] { "Statecraft", "-map-look" }, webgl: false));
        Assert.IsFalse(MapLook.Requested(null, new[] { "-map-looks" }, webgl: false));
    }

    [Test]
    public void WithoutTheSwitchNothingIsPlacedOrRegistered()
    {
        var map = BuiltMap();
        MapLook.Apply(map, false);
        Assert.IsNull(map.transform.Find(MapLook.RootName));
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-")));
        Assert.AreEqual(1f, TiltShift.RadiusScale);
        Assert.AreEqual(MapGrade.Saturation, Grade().saturation.value);
    }

    [Test]
    public void ThePictureChangesUnderTheSwitchAndIsRestoredExactly()
    {
        var map = BuiltMap();
        MapLook.Apply(map, true);
        Assert.AreEqual(MapLook.Saturation, Grade().saturation.value);
        Assert.AreEqual(MapLook.TiltRadiusScale, TiltShift.RadiusScale);
        Assert.IsTrue(VisualToggles.Names.Contains(MapLook.GradeToggle));
        MapLook.Apply(map, false);
        Assert.AreEqual(MapGrade.Saturation, Grade().saturation.value);
        Assert.AreEqual(MapGrade.Contrast, Grade().contrast.value);
        Assert.AreEqual(MapGrade.PostExposure, Grade().postExposure.value);
        Assert.AreEqual(1f, TiltShift.RadiusScale);
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-")));
    }

    [Test]
    public void TheRadiusScaleScalesOnlyTheBlurRadius()
    {
        var plain = TiltShift.Params(0.1f, 30000f);
        TiltShift.RadiusScale = 0.5f;
        try { var scaled = TiltShift.Params(0.1f, 30000f); Assert.AreEqual(plain.w * 0.5f, scaled.w, 1e-7f); Assert.AreEqual(plain.x, scaled.x); }
        finally { TiltShift.Reset(); }
        Assert.AreEqual(1f, TiltShift.RadiusScale);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run the EditMode command above. Expected: compile errors (`MapLook` and `TiltShift.RadiusScale` do not exist), so the run
reports failure.

- [ ] **Step 3: Add `RadiusScale` to `TiltShift.cs`**

In `unity/Assets/Scripts/Map/TiltShift.cs`, after `public static bool Enabled = true;` add:

```csharp
        /// <summary>Scales the blur radius; 1 except under the opt-in map look (MapLook). Reset puts it back.</summary>
        public static float RadiusScale = 1f;
```

Change `Params` to:

```csharp
        public static Vector4 Params(float topInset, float distance) =>
            new Vector4(FocusCentre(topInset), HalfWidth, Feather, MaxRadiusUv * Strength(distance) * RadiusScale);
```

In `Reset()`, after `Enabled = true;` add `RadiusScale = 1f;`.

- [ ] **Step 4: Create `unity/Assets/Scripts/Map/MapLook.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Statecraft.Game.Map
{
    /// <summary>Opt-in map look (spec docs/superpowers/specs/2026-10-12-look-and-play-two-options-design.md, Step 0):
    /// orchards, groves, fields and rocks by biome (MapLookScatter), town blocks (MapLookTowns), the 1948 railway
    /// (MapLookLines), and a livelier grade with a lighter tilt-shift, all under one "MapLook" root beside "Settlements".
    /// Web: ?map-look or ?map-look=1. Desktop: -map-look. Off with nothing applied touches nothing, so the shipped map is
    /// unchanged. No simulation state is read or written.</summary>
    public static class MapLook
    {
        public const string RootName = "MapLook";
        public const string ScatterToggle = "look-scatter", TownsToggle = "look-towns", LinesToggle = "look-lines", GradeToggle = "look-grade";
        /// <summary>Starting values, tuned from matched captures (plan Task 8); the shipped grade is MapGrade's.</summary>
        public const float Saturation = 18f, Contrast = 22f, Temperature = 9f, PostExposure = 0.2f, TiltRadiusScale = 0.45f;

        static readonly Regex Flag = new Regex(@"(?:^|&)map-look(?:=1)?(?=&|$)", RegexOptions.CultureInvariant);
        static readonly List<Action> Unregister = new List<Action>();
        static bool applied;

        public static bool Requested() =>
            Requested(Application.absoluteURL, Environment.GetCommandLineArgs(), Application.platform == RuntimePlatform.WebGLPlayer);

        /// <summary>Web: only a top-level query parameter counts, bare or "=1", never a fragment or a nested URL's question
        /// mark (BuildingPreview's rule). Desktop: only the exact argument.</summary>
        public static bool Requested(string? url, string[] args, bool webgl)
        {
            if (!webgl) return Array.IndexOf(args, "-map-look") >= 0;
            var address = (url ?? "").Split('#')[0];
            var start = address.IndexOf('?');
            return start >= 0 && Flag.IsMatch(address.Substring(start + 1));
        }

        /// <summary>Removes what an earlier Apply placed and restores the picture; then, when <paramref name="enabled"/>, builds
        /// the layers and applies the look's picture. Off with nothing applied changes nothing (the default path).</summary>
        public static void Apply(MapScene map, bool enabled)
        {
            foreach (var undo in Unregister) undo();
            Unregister.Clear();
            var rig = Camera.main != null ? Camera.main.GetComponent<CameraRig>() : null;
            if (map != null) { var old = map.transform.Find(RootName); if (old != null) BuildingPreview.Discard(old.gameObject); }
            if (!enabled) { if (applied) Picture(rig, false); applied = false; return; }
            if (map == null) return;
            var root = new GameObject(RootName).transform;
            root.SetParent(map.transform, false);
            Unregister.Add(VisualToggles.Register(GradeToggle, on => Picture(rig, on)));
            Picture(rig, true);
            applied = true;
        }

        static void Picture(CameraRig? rig, bool look)
        {
            TiltShift.RadiusScale = look ? TiltRadiusScale : 1f;
            if (rig == null || rig.Volume == null || rig.Volume.profile == null) return;
            var p = rig.Volume.profile;
            if (p.TryGet(out ColorAdjustments ca))
            {
                ca.saturation.Override(look ? Saturation : MapGrade.Saturation);
                ca.contrast.Override(look ? Contrast : MapGrade.Contrast);
                ca.postExposure.Override(look ? PostExposure : MapGrade.PostExposure);
            }
            if (p.TryGet(out WhiteBalance wb)) wb.temperature.Override(look ? Temperature : MapGrade.Temperature);
        }
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run the EditMode command. Expected: `result="Passed"` and every MapLookTests case passed. If
`ThePictureChanges…` fails because `Camera.main` has no `CameraRig` after `map.Build()`, read `MapScene.cs:120-135`
for how the rig is attached and make `BuiltMap` match it.

- [ ] **Step 6: Commit**

```bash
git checkout -- unity/Assets/Settings unity/ProjectSettings/ProjectSettings.asset unity/ProjectSettings/QualitySettings.asset
git add unity/Assets/Scripts/Map/MapLook.cs unity/Assets/Scripts/Map/MapLook.cs.meta unity/Assets/Scripts/Map/TiltShift.cs unity/Assets/Tests/EditMode/MapLookTests.cs unity/Assets/Tests/EditMode/MapLookTests.cs.meta
git commit -m "feat(map): opt-in map-look switch with a livelier grade and lighter blur

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Shapes and assets

**Files:**
- Create: `unity/Assets/Scripts/Map/MapLookShapes.cs`, `unity/Assets/Scripts/Map/MapLookAssets.cs`
- Test: `unity/Assets/Tests/EditMode/MapLookTests.cs`

- [ ] **Step 1: Add the failing tests** (append inside `MapLookTests`)

```csharp
    [Test]
    public void EveryShapeStandsOnTheOriginOneUnitTallWithinAUnitFootprint()
    {
        foreach (var name in MapLookShapes.All)
        {
            var parts = MapLookShapes.Build(name);
            try
            {
                Assert.IsNotEmpty(parts, name);
                var b = parts[0].Mesh.bounds;
                foreach (var p in parts) b.Encapsulate(p.Mesh.bounds);
                Assert.AreEqual(0f, b.min.y, 1e-4f, name);
                Assert.AreEqual(1f, b.max.y, 1e-4f, name);
                Assert.LessOrEqual(Mathf.Max(b.size.x, b.size.z), 1.01f, name);
                foreach (var p in parts) Assert.IsTrue(MapLookAssets.Palette.ContainsKey(p.Colour), $"{name}: no colour '{p.Colour}'");
            }
            finally { foreach (var p in parts) Object.DestroyImmediate(p.Mesh); }
        }
    }

    [Test]
    public void SideFacesPointOutward()
    {
        foreach (var name in new[] { MapLookShapes.Olive, MapLookShapes.BlockPlaster })
        {
            var parts = MapLookShapes.Build(name);
            var mesh = parts[parts.Length - 1].Mesh;
            var v = mesh.vertices; var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                if (Mathf.Abs(n.y) > 0.9f) { Assert.IsTrue(n.y > 0f == (v[t[i]].y > 0.5f), $"{name}: a cap faces inward"); continue; }
                var centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                Assert.Greater(Vector3.Dot(n, new Vector3(centre.x, 0f, centre.z)), 0f, $"{name}: a side faces inward");
            }
            foreach (var p in parts) Object.DestroyImmediate(p.Mesh);
        }
    }
```

Note: Unity's front face is clockwise, so `Cross(b - a, c - a)` points toward the viewer that sees the face.

- [ ] **Step 2: Run and confirm failure** (compile errors: `MapLookShapes`, `MapLookAssets` missing).

- [ ] **Step 3: Create `unity/Assets/Scripts/Map/MapLookShapes.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>Faceted low-poly meshes for the opt-in map look, built in code (no asset file, download or licence). A shape
    /// is one or more parts, each a mesh and a palette key (MapLookAssets.Palette). Every shape's base is at y = 0, its top
    /// at y = 1 and its footprint centred on the origin within a unit square, so an instance's scale is its size in
    /// metres.</summary>
    public static class MapLookShapes
    {
        public readonly struct Part
        {
            public readonly Mesh Mesh; public readonly string Colour;
            public Part(Mesh mesh, string colour) { Mesh = mesh; Colour = colour; }
        }

        public const string Olive = "olive", Citrus = "citrus", Cypress = "cypress", Palm = "palm", Rock = "rock",
            FieldWheat = "field-wheat", FieldGreen = "field-green", BlockPlaster = "block-plaster", BlockStone = "block-stone";
        public static readonly string[] All = { Olive, Citrus, Cypress, Palm, Rock, FieldWheat, FieldGreen, BlockPlaster, BlockStone };

        /// <summary>New meshes the caller owns (MapLookAssets destroys them with the root).</summary>
        public static Part[] Build(string shape) => shape switch
        {
            Olive => new[] { new Part(Trunk(0.06f, 0.35f), "bark"),
                new Part(new MeshBuilder().Frustum(6, 0.3f, 0.3f, 0.6f, 0.48f, false, true).Frustum(6, 0.6f, 0.48f, 1f, 0.18f, true, false).ToMesh(shape), "olive") },
            Citrus => new[] { new Part(Trunk(0.05f, 0.2f), "bark"),
                new Part(new MeshBuilder().Frustum(7, 0.15f, 0.3f, 0.45f, 0.45f, false, true).Frustum(7, 0.45f, 0.45f, 1f, 0.12f, true, false).ToMesh(shape), "citrus") },
            Cypress => new[] { new Part(Trunk(0.04f, 0.12f), "bark"),
                new Part(new MeshBuilder().Frustum(6, 0.1f, 0.1f, 0.35f, 0.17f, false, true).Frustum(6, 0.35f, 0.17f, 1f, 0f, false, false).ToMesh(shape), "cypress") },
            Palm => new[] { new Part(Trunk(0.03f, 0.85f), "bark"),
                new Part(new MeshBuilder().Frustum(8, 0.8f, 0.04f, 0.88f, 0.42f, false, true).Frustum(8, 0.88f, 0.42f, 1f, 0.05f, true, false).ToMesh(shape), "palm") },
            Rock => new[] { new Part(new MeshBuilder().Frustum(5, 0f, 0.5f, 1f, 0.28f, true, false).ToMesh(shape), "rock") },
            FieldWheat => new[] { new Part(UnitBox(shape), "wheat") },
            FieldGreen => new[] { new Part(UnitBox(shape), "green") },
            BlockPlaster => new[] { new Part(UnitBox(shape), "plaster") },
            BlockStone => new[] { new Part(UnitBox(shape), "stone") },
            _ => throw new ArgumentException($"unknown shape '{shape}'", nameof(shape)),
        };

        static Mesh Trunk(float radius, float height) => new MeshBuilder().Frustum(5, 0f, radius, height, radius * 0.8f, false, false).ToMesh("trunk");

        /// <summary>A 1 x 1 x 1 box: a four-sided frustum turned 45 degrees, half-diagonal 0.7071.</summary>
        static Mesh UnitBox(string name) => new MeshBuilder().Frustum(4, 0f, 0.70710678f, 1f, 0.70710678f, true, true, 45f).ToMesh(name);

        /// <summary>Unshared vertices per triangle, so RecalculateNormals gives flat facets. Unity's front face is clockwise
        /// as seen by the viewer; the windings below were derived for a viewer outside, above (top cap) and below (bottom
        /// cap), and SideFacesPointOutward checks them.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int> t = new List<int>();

            public MeshBuilder Frustum(int sides, float y0, float r0, float y1, float r1, bool capTop, bool capBottom, float startDeg = 0f)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = (startDeg + 360f * i / sides) * Mathf.Deg2Rad, a1 = (startDeg + 360f * (i + 1) / sides) * Mathf.Deg2Rad;
                    Vector3 p00 = At(a0, r0, y0), p01 = At(a1, r0, y0), p10 = At(a0, r1, y1), p11 = At(a1, r1, y1);
                    if (r1 > 0f) Tri(p10, p11, p01);
                    if (r0 > 0f) Tri(p10, p01, p00);
                    if (capTop && r1 > 0f) Tri(new Vector3(0f, y1, 0f), p11, p10);
                    if (capBottom && r0 > 0f) Tri(new Vector3(0f, y0, 0f), p00, p01);
                }
                return this;
            }

            void Tri(Vector3 a, Vector3 b, Vector3 c) { int n = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(n); t.Add(n + 1); t.Add(n + 2); }
            static Vector3 At(float a, float r, float y) => new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = "look-" + name };
                m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
                return m;
            }
        }
    }
}
```

- [ ] **Step 4: Create `unity/Assets/Scripts/Map/MapLookAssets.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Statecraft.Game.Map
{
    /// <summary>Lives on the MapLook root and owns everything the look creates at run time (materials and meshes), destroying
    /// it with the root. Emit combines placed shapes into one renderer per ChunkM chunk and palette colour, so the map gets
    /// a few hundred static renderers that Unity culls by chunk, with no per-frame code.</summary>
    public sealed class MapLookAssets : MonoBehaviour
    {
        public const float ChunkM = 20000f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Smoothness = Shader.PropertyToID("_Smoothness");

        /// <summary>Colours tuned under the map light (start values; plan Task 8 tunes them from captures).</summary>
        public static readonly IReadOnlyDictionary<string, Color> Palette = new Dictionary<string, Color>
        {
            ["bark"] = new Color(0.36f, 0.27f, 0.20f), ["olive"] = new Color(0.47f, 0.52f, 0.36f),
            ["citrus"] = new Color(0.18f, 0.36f, 0.17f), ["cypress"] = new Color(0.16f, 0.30f, 0.18f),
            ["palm"] = new Color(0.33f, 0.48f, 0.22f), ["rock"] = new Color(0.62f, 0.57f, 0.50f),
            ["wheat"] = new Color(0.86f, 0.74f, 0.42f), ["green"] = new Color(0.55f, 0.62f, 0.30f),
            ["plaster"] = new Color(0.95f, 0.93f, 0.87f), ["stone"] = new Color(0.84f, 0.75f, 0.58f),
            ["rail"] = new Color(0.27f, 0.22f, 0.18f), ["road"] = new Color(0.80f, 0.72f, 0.56f),
        };

        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        readonly Dictionary<string, MapLookShapes.Part[]> shapes = new Dictionary<string, MapLookShapes.Part[]>(StringComparer.Ordinal);
        readonly List<Mesh> owned = new List<Mesh>();
        Material? template;

        /// <summary>The template is the map's marker material (URP Lit); each colour is a copy of it.</summary>
        public void Init(Material markerTemplate) => template = markerTemplate;

        public Material MaterialFor(string colour)
        {
            if (materials.TryGetValue(colour, out var m)) return m;
            m = new Material(template ?? throw new InvalidOperationException("MapLookAssets.Init was not called")) { name = "look-" + colour };
            m.SetColor(BaseColor, Palette[colour]);
            if (m.HasProperty(Smoothness)) m.SetFloat(Smoothness, 0.05f);
            return materials[colour] = m;
        }

        public MapLookShapes.Part[] Shape(string name) =>
            shapes.TryGetValue(name, out var parts) ? parts : shapes[name] = MapLookShapes.Build(name);

        public Mesh Own(Mesh mesh) { owned.Add(mesh); return mesh; }

        /// <summary>One renderer per (chunk, colour) under <paramref name="parent"/>, named "&lt;layer&gt;-&lt;cx&gt;_&lt;cz&gt;/&lt;colour&gt;";
        /// the matrices are world space (the map stands at the origin). Returns the number of renderers.</summary>
        public int Emit(Transform parent, string layer, IEnumerable<(string shape, Matrix4x4 matrix)> items, bool shadows)
        {
            var groups = new SortedDictionary<string, List<CombineInstance>>(StringComparer.Ordinal);
            foreach (var (shape, matrix) in items)
            {
                var at = matrix.GetColumn(3);
                var chunk = $"{Mathf.FloorToInt(at.x / ChunkM)}_{Mathf.FloorToInt(at.z / ChunkM)}";
                foreach (var part in Shape(shape))
                {
                    var key = chunk + "/" + part.Colour;
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = part.Mesh, transform = matrix });
                }
            }
            foreach (var kv in groups)
            {
                var mesh = Own(new Mesh { name = $"look-{layer}-{kv.Key}", indexFormat = IndexFormat.UInt32 });
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject($"{layer}-{kv.Key}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = MaterialFor(kv.Key.Substring(kv.Key.IndexOf('/') + 1));
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            return groups.Count;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) Kill(m);
            foreach (var parts in shapes.Values) foreach (var p in parts) Kill(p.Mesh);
            foreach (var m in owned) Kill(m);
        }

        static void Kill(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) Destroy(o); else DestroyImmediate(o); }
    }
}
```

- [ ] **Step 5: Run and confirm the tests pass.** If `SideFacesPointOutward` fails, swap the last two arguments of the
failing `Tri` call (winding) and re-run; do not change the test.

- [ ] **Step 6: Commit** (revert the settings first, as in Task 1; add both `.cs` files, their `.meta` files and the test file):
`feat(map): low-poly shapes and chunked combine for the map look`.

---

### Task 3: Scatter by biome

**Files:**
- Create: `unity/Assets/Scripts/Map/MapLookScatter.cs`
- Modify: `unity/Assets/Scripts/Map/MapLook.cs` (wire the layer)
- Test: `unity/Assets/Tests/EditMode/MapLookTests.cs`

- [ ] **Step 1: Add the failing tests** (append inside `MapLookTests`)

```csharp
    static HexFile TestHexes() => new HexFile
    {
        hex_size_km = 5f,
        hexes = new[]
        {
            new HexJson { q = 0, r = 0, lon = 34.80, lat = 32.00, land = true, biome = "coastal_plain" },
            new HexJson { q = 1, r = 0, lon = 34.90, lat = 32.00, land = true, biome = "hills" },
            new HexJson { q = 2, r = 0, lon = 35.00, lat = 32.00, land = true, biome = "valley" },
            new HexJson { q = 3, r = 0, lon = 34.70, lat = 32.00, land = false, biome = "sea" },
        },
    };
    static readonly GeoFrame TestFrame = new GeoFrame(34.6, 35.2, 31.8, 32.2, 56000f, 44000f);

    [Test]
    public void TheScatterIsDeterministicAndRespectsSeaSlopeAndSettlements()
    {
        float seaY = 0f;
        var sea = TestFrame.ToWorld(34.75, 32.0, 0f).x;           // everything west of this is sea
        float Ground(Vector3 p) => p.x < sea ? -50f : 100f;
        float Slope(Vector3 p) => 0f;
        var town = TestFrame.ToWorld(34.80, 32.00, 0f);
        var avoid = new[] { (town, 2000f) };
        var a = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, Slope, seaY, avoid);
        var b = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, Slope, seaY, avoid);
        Assert.Greater(a.Count, 50);
        Assert.AreEqual(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) { Assert.AreEqual(a[i].At, b[i].At); Assert.AreEqual(a[i].Shape, b[i].Shape); }
        foreach (var p in a)
        {
            Assert.GreaterOrEqual(p.At.x, sea - 1f, "planted in the sea");
            Assert.GreaterOrEqual(new Vector2(p.At.x - town.x, p.At.z - town.z).magnitude, 2000f, "planted in a settlement");
        }
        Assert.IsTrue(a.Any(p => p.Shape == MapLookShapes.Citrus) && a.Any(p => p.Shape == MapLookShapes.Olive) && a.Any(p => p.Shape == MapLookShapes.FieldWheat));
    }

    [Test]
    public void TreesSkipSteepGroundAndFieldsSkipUnevenGround()
    {
        float Ground(Vector3 p) => Mathf.Floor(p.x / 300f) * 200f;  // 200 m terraces every 300 m: never flat enough for a field
        var steep = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, _ => 40f, -1000f, new (Vector3, float)[0]);
        Assert.IsEmpty(steep.Where(p => p.Shape != MapLookShapes.FieldWheat && p.Shape != MapLookShapes.FieldGreen));
        var uneven = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, _ => 0f, -1000f, new (Vector3, float)[0]);
        Assert.IsEmpty(uneven.Where(p => p.Shape == MapLookShapes.FieldWheat || p.Shape == MapLookShapes.FieldGreen));
    }

    [Test]
    public void TheLookPlacesLayersOnTheRealMapAndRemovesThem()
    {
        var map = BuiltMap();
        MapLook.Apply(map, true);
        var root = map.transform.Find(MapLook.RootName);
        Assert.IsNotNull(root);
        Assert.Greater(root.GetComponentsInChildren<MeshRenderer>().Length, 10);
        Assert.IsTrue(VisualToggles.Names.Contains(MapLook.ScatterToggle));
        VisualToggles.Set(MapLook.ScatterToggle, false);
        Assert.IsFalse(root.Find(MapLook.ScatterToggle).gameObject.activeSelf);
        MapLook.Apply(map, false);
        Assert.IsNull(map.transform.Find(MapLook.RootName));
    }
```

- [ ] **Step 2: Run and confirm failure** (compile error: `MapLookScatter` missing).

- [ ] **Step 3: Create `unity/Assets/Scripts/Map/MapLookScatter.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>The map look's vegetation, fields and rocks. Plan is pure: per land hex, groups of a shape (a grove, a row,
    /// a field) by the hex's biome, from a generator seeded by (q, r) alone, so a hex always gets the same planting
    /// whatever the order. Diorama scale: a tree stands a few hundred metres tall, as the buildings and figures do
    /// (SettlementPlacer, PreviewProps).</summary>
    public static class MapLookScatter
    {
        public readonly struct Plant
        {
            public readonly string Shape; public readonly Vector3 At; public readonly float Yaw; public readonly Vector3 Scale;
            public Plant(string shape, Vector3 at, float yaw, Vector3 scale) { Shape = shape; At = at; Yaw = yaw; Scale = scale; }
            public Matrix4x4 Matrix => Matrix4x4.TRS(At, Quaternion.Euler(0f, Yaw, 0f), Scale);
        }

        public const int Seed = 1948;
        /// <summary>Multiplies every rule's group count; tuned from captures and the frame budget (plan Tasks 7-8).</summary>
        public const float DensityScale = 1f;
        public const float MaxTreeSlopeDeg = 28f, MaxFieldRiseM = 35f, SeaClearanceM = 5f, AvoidShare = 2.8f;
        public const float OliveM = 260f, CitrusM = 220f, CypressM = 480f, PalmM = 560f, RockM = 170f, FieldDepthM = 16f;

        // Per biome: (shape, groups per hex, shapes per group, spacing in a group). A group of 9 is a 3 x 3 grove.
        static readonly Dictionary<string, (string shape, int groups, int per, float spacing)[]> Rules =
            new Dictionary<string, (string, int, int, float)[]>(StringComparer.Ordinal)
        {
            ["coastal_plain"] = new[] { (MapLookShapes.Citrus, 3, 9, 420f), (MapLookShapes.Palm, 2, 3, 500f), (MapLookShapes.FieldWheat, 2, 1, 0f), (MapLookShapes.FieldGreen, 2, 1, 0f), (MapLookShapes.Olive, 1, 4, 500f) },
            ["valley"] = new[] { (MapLookShapes.FieldWheat, 4, 1, 0f), (MapLookShapes.FieldGreen, 3, 1, 0f), (MapLookShapes.Olive, 2, 5, 500f) },
            ["hills"] = new[] { (MapLookShapes.Olive, 4, 6, 450f), (MapLookShapes.Cypress, 3, 2, 400f), (MapLookShapes.Rock, 2, 2, 400f) },
            ["mountains"] = new[] { (MapLookShapes.Cypress, 3, 3, 420f), (MapLookShapes.Rock, 4, 2, 400f), (MapLookShapes.Olive, 1, 4, 450f) },
            ["desert"] = new[] { (MapLookShapes.Rock, 3, 2, 500f) },
        };

        public static List<Plant> Plan(HexFile hexes, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
            float seaY, IReadOnlyList<(Vector3 at, float radius)> avoid)
        {
            var plants = new List<Plant>();
            float hexM = hexes.hex_size_km * 1000f;
            foreach (var h in hexes.hexes)
            {
                if (!h.land || h.biome == null || !Rules.TryGetValue(h.biome, out var rules)) continue;
                var rng = new System.Random(Seed ^ (h.q * 73856093) ^ (h.r * 19349663));
                var centre = frame.ToWorld(h.lon, h.lat, 0f);
                foreach (var (shape, groups, per, spacing) in rules)
                {
                    int count = Mathf.RoundToInt(groups * DensityScale);
                    for (int g = 0; g < count; g++)
                    {
                        var origin = centre + Disc(rng, 0.8f * hexM);
                        var row = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
                        for (int i = 0; i < per; i++)
                        {
                            var offset = per == 1 ? Vector3.zero : row * new Vector3((i % 3 - 1) * spacing, 0f, (i / 3 - 1) * spacing) + Disc(rng, spacing * 0.2f);
                            if (TryPlant(shape, origin + offset, rng, groundY, slopeDeg, seaY, avoid, out var plant)) plants.Add(plant);
                        }
                    }
                }
            }
            return plants;
        }

        static bool TryPlant(string shape, Vector3 p, System.Random rng, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
            float seaY, IReadOnlyList<(Vector3 at, float radius)> avoid, out Plant plant)
        {
            plant = default;
            float yaw = (float)rng.NextDouble() * 360f, k = 0.8f + 0.4f * (float)rng.NextDouble();
            float w = 700f + 600f * (float)rng.NextDouble(), depth = 700f + 600f * (float)rng.NextDouble();
            float y = groundY(p);
            if (y < seaY + SeaClearanceM) return false;
            foreach (var (at, radius) in avoid)
                if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < radius * radius) return false;
            if (shape == MapLookShapes.FieldWheat || shape == MapLookShapes.FieldGreen)
            {
                // A flat box: its top just above the highest corner, its bottom below the lowest, so it never floats.
                var turn = Quaternion.Euler(0f, yaw, 0f);
                float lo = y, hi = y;
                foreach (var c in new[] { new Vector3(w, 0f, depth), new Vector3(-w, 0f, depth), new Vector3(w, 0f, -depth), new Vector3(-w, 0f, -depth) })
                {
                    float cy = groundY(p + turn * (c * 0.5f));
                    if (cy < seaY + SeaClearanceM) return false;
                    lo = Mathf.Min(lo, cy); hi = Mathf.Max(hi, cy);
                }
                if (hi - lo > MaxFieldRiseM) return false;
                plant = new Plant(shape, new Vector3(p.x, lo - FieldDepthM, p.z), yaw, new Vector3(w, hi - lo + FieldDepthM + 6f, depth));
                return true;
            }
            if (slopeDeg(p) > MaxTreeSlopeDeg) return false;
            float size = SizeOf(shape) * k;
            plant = new Plant(shape, new Vector3(p.x, y - 0.03f * size, p.z), yaw, Vector3.one * size);
            return true;
        }

        static float SizeOf(string shape) => shape switch
        {
            MapLookShapes.Olive => OliveM, MapLookShapes.Citrus => CitrusM, MapLookShapes.Cypress => CypressM,
            MapLookShapes.Palm => PalmM, MapLookShapes.Rock => RockM,
            _ => throw new ArgumentException($"no size for '{shape}'", nameof(shape)),
        };

        static Vector3 Disc(System.Random rng, float radius)
        {
            double a = rng.NextDouble() * 2 * Math.PI, d = radius * Math.Sqrt(rng.NextDouble());
            return new Vector3((float)(d * Math.Cos(a)), 0f, (float)(d * Math.Sin(a)));
        }

        /// <summary>Each settlement keeps a clear disc of AvoidShare x its marker edge.</summary>
        public static List<(Vector3 at, float radius)> Avoid(MapScene map) => map.Settlements
            .Where(s => !double.IsNaN(s.Lon) && !double.IsNaN(s.Lat))
            .Select(s => (map.Frame.ToWorld(s.Lon, s.Lat, 0f), AvoidShare * SettlementPlacer.MarkerHeightM(s.Type))).ToList();

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root)
        {
            var go = new GameObject(MapLook.ScatterToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var data = t.terrainData; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            float Slope(Vector3 p) => data.GetSteepness((p.x - origin.x) / data.size.x, (p.z - origin.z) / data.size.z);
            var plants = Plan(map.Hexes, map.Frame, Ground, Slope, PreviewProps.SeaLevelY(), Avoid(map));
            assets.Emit(go.transform, "scatter", plants.Select(p => (p.Shape, p.Matrix)), shadows: true);
            return go;
        }
    }
}
```

- [ ] **Step 4: Wire the layer into `MapLook.Apply`.** Replace the line `Unregister.Add(VisualToggles.Register(GradeToggle, on => Picture(rig, on)));` (right after
`root.SetParent(map.transform, false);`) with:

```csharp
            var assets = root.gameObject.AddComponent<MapLookAssets>();
            assets.Init(map.MarkerMaterial);
            var scatter = MapLookScatter.Build(map, assets, root);
            Unregister.Add(VisualToggles.Register(ScatterToggle, on => { if (scatter != null) scatter.SetActive(on); }));
            Unregister.Add(VisualToggles.Register(GradeToggle, on => Picture(rig, on)));
```

- [ ] **Step 5: Run and confirm all MapLookTests pass.** If `TestFrame.ToWorld` puts x somewhere unexpected, read
`GeoFrame.cs:11-15` and adjust only the test's sea line, not the plan.

- [ ] **Step 6: Commit** (revert the settings first; add the new file, its `.meta`, `MapLook.cs` and the tests):
`feat(map): biome scatter of orchards, groves, fields and rocks for the map look`.

---

### Task 4: Town blocks

**Files:**
- Create: `unity/Assets/Scripts/Map/MapLookTowns.cs`
- Modify: `unity/Assets/Scripts/Map/MapLook.cs`
- Test: `unity/Assets/Tests/EditMode/MapLookTests.cs`

- [ ] **Step 1: Add the failing test**

```csharp
    [Test]
    public void TownBlocksRingTheMarkerAndStayUnderItsLabel()
    {
        var s = new Statecraft.Game.Map.Settlement { Id = "tel_aviv", Name = "Tel Aviv", Type = "city", Lon = 34.78, Lat = 32.08 };
        float h = SettlementPlacer.MarkerHeightM("city");
        var centre = TestFrame.ToWorld(s.Lon, s.Lat, 0f);
        var blocks = MapLookTowns.Plan(new[] { s }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => 50f, _ => 0f, 0f);
        Assert.Greater(blocks.Count, 20);
        foreach (var b in blocks)
        {
            Assert.GreaterOrEqual(new Vector2(b.At.x - centre.x, b.At.z - centre.z).magnitude, MapLookTowns.InnerShare * h - 1f);
            Assert.LessOrEqual(b.At.y + b.Scale.y, 50f + MapLookTowns.MaxHeightShare * h + 1f);
        }
        CollectionAssert.AreEqual(blocks.Select(b => b.At), MapLookTowns.Plan(new[] { s }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => 50f, _ => 0f, 0f).Select(b => b.At));
    }
```

If `Settlement` is ambiguous with a `Statecraft.Sim` type in the test assembly, keep the fully qualified name shown.

- [ ] **Step 2: Run and confirm failure.**

- [ ] **Step 3: Create `unity/Assets/Scripts/Map/MapLookTowns.cs`**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>The map look's town blocks: a ring of flat-roofed blocks around each city, town and arab town, outside the
    /// marker's own footprint (where the box or prototype stands) and under its label (MarkerHeightM). White plaster for
    /// the Jewish cities and towns (Tel Aviv's White City, Haifa), sandstone for arab towns. Seeded by the settlement id
    /// through a stable hash, so every launch draws the same streets.</summary>
    public static class MapLookTowns
    {
        public const float InnerShare = 1.05f, OuterShare = 2.6f, MaxHeightShare = 0.55f, MinHeightM = 110f, MaxSlopeDeg = 20f, SinkM = 20f;

        static readonly Dictionary<string, (int count, string shape)> ByType = new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["city"] = (46, MapLookShapes.BlockPlaster), ["town"] = (22, MapLookShapes.BlockPlaster), ["arab_town"] = (16, MapLookShapes.BlockStone),
        };

        public static List<MapLookScatter.Plant> Plan(IEnumerable<Settlement> settlements, Func<double, double, Vector3> toWorld,
            Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg, float seaY)
        {
            var blocks = new List<MapLookScatter.Plant>();
            foreach (var s in settlements)
            {
                if (!ByType.TryGetValue(s.Type, out var rule) || double.IsNaN(s.Lon) || double.IsNaN(s.Lat)) continue;
                float h = SettlementPlacer.MarkerHeightM(s.Type);
                var centre = toWorld(s.Lon, s.Lat);
                var rng = new System.Random(StableHash(s.Id));
                for (int i = 0; i < rule.count; i++)
                {
                    float a = (float)(rng.NextDouble() * 2 * Math.PI), r = Mathf.Lerp(InnerShare * h, OuterShare * h, Mathf.Sqrt((float)rng.NextDouble()));
                    float w = 140f + 220f * (float)rng.NextDouble(), d = 140f + 220f * (float)rng.NextDouble();
                    float t = (float)rng.NextDouble(), height = Mathf.Lerp(MinHeightM, MaxHeightShare * h, t * t);
                    float yaw = 10f + (rng.NextDouble() < 0.8 ? 0f : 45f);
                    var p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    float y = groundY(p);
                    if (y < seaY + MapLookScatter.SeaClearanceM || slopeDeg(p) > MaxSlopeDeg) continue;
                    blocks.Add(new MapLookScatter.Plant(rule.shape, new Vector3(p.x, y - SinkM, p.z), yaw, new Vector3(w, height + SinkM, d)));
                }
            }
            return blocks;
        }

        /// <summary>FNV-1a over the id's characters: stable across runs and platforms (string.GetHashCode is not).</summary>
        public static int StableHash(string s) { unchecked { uint h = 2166136261; foreach (char c in s) h = (h ^ c) * 16777619; return (int)h; } }

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root)
        {
            var go = new GameObject(MapLook.TownsToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var data = t.terrainData; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            float Slope(Vector3 p) => data.GetSteepness((p.x - origin.x) / data.size.x, (p.z - origin.z) / data.size.z);
            var blocks = Plan(map.Settlements, (lon, lat) => map.Frame.ToWorld(lon, lat, 0f), Ground, Slope, PreviewProps.SeaLevelY());
            assets.Emit(go.transform, "towns", blocks.Select(b => (b.Shape, b.Matrix)), shadows: true);
            return go;
        }
    }
}
```

The block's top is `y - SinkM + height + SinkM = y + height`, and `height` is at most `MaxHeightShare * h`, which is
what the test checks.

- [ ] **Step 4: Wire it.** In `MapLook.Apply`, after the scatter toggle line, add:

```csharp
            var towns = MapLookTowns.Build(map, assets, root);
            Unregister.Add(VisualToggles.Register(TownsToggle, on => { if (towns != null) towns.SetActive(on); }));
```

- [ ] **Step 5: Run, confirm pass.**

- [ ] **Step 6: Commit:** `feat(map): town block clusters for the map look`. Revert the settings first.

---

### Task 5: The 1948 railway and the Jerusalem road

**Files:**
- Create: `unity/Assets/Scripts/Map/MapLookLines.cs`
- Modify: `unity/Assets/Scripts/Map/MapLook.cs`
- Test: `unity/Assets/Tests/EditMode/MapLookTests.cs`

- [ ] **Step 1: Verify the route points before writing them.** The values below are approximations. Check each one against
a citable map and correct any that is off by more than about 1 km. Sources:
  - the Survey of Palestine 1:250,000 sheets (1946);
  - Wikipedia "Jaffa–Jerusalem railway", "Eastern Railway (Palestine)" and "Palestine Railways".

Record the sources you used in the class summary.

  - Rail, Jaffa–Jerusalem (1892):
    - Jaffa 34.765, 32.054; Lydda 34.882, 31.946; Ramla 34.872, 31.928; Na'ana 34.860, 31.875;
    - Sejed 34.920, 31.820; Artuf 34.980, 31.770; Deir esh-Sheikh 35.040, 31.750; Bar Giora 35.080, 31.730;
    - Battir 35.135, 31.730; Jerusalem 35.225, 31.765.
  - Rail, Lydda–Haifa (the WWI military line via Tulkarm):
    - Lydda 34.882, 31.946; Ras el-Ein 34.955, 32.095; Qalqilya 34.975, 32.190; Tulkarm 35.020, 32.315;
    - Hadera 34.920, 32.440; Binyamina 34.950, 32.520; Atlit 34.940, 32.690; Haifa 34.990, 32.820.
  - Rail, Lydda–Gaza: Lydda 34.882, 31.946; Yavne 34.740, 31.880; Isdud 34.660, 31.750; Majdal 34.580, 31.670;
    Gaza 34.460, 31.510.
  - Road, Tel Aviv–Jerusalem: Tel Aviv 34.780, 32.080; Ramla 34.872, 31.928; Latrun 34.980, 31.840;
    Bab el-Wad 35.010, 31.810; Jerusalem 35.210, 31.780.

- [ ] **Step 2: Add the failing test**

```csharp
    [Test]
    public void ARibbonFacesUpAndFollowsTheGround()
    {
        var path = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2000f), new Vector3(1000f, 0f, 3000f) };
        var mesh = MapLookLines.Ribbon(path, 140f, p => p.z * 0.01f);
        try
        {
            Assert.Greater(mesh.vertexCount, 10);
            foreach (var n in mesh.normals) Assert.Greater(n.y, 0.5f);
            foreach (var v in mesh.vertices) Assert.AreEqual(v.z * 0.01f + MapLookLines.LiftM, v.y, 0.01f);
        }
        finally { Object.DestroyImmediate(mesh); }
        foreach (var r in MapLookLines.Routes) { Assert.GreaterOrEqual(r.Points.Length, 2, r.Id); Assert.IsTrue(MapLookAssets.Palette.ContainsKey(r.Colour), r.Id); }
    }
```

- [ ] **Step 3: Run, confirm failure.**

- [ ] **Step 4: Create `unity/Assets/Scripts/Map/MapLookLines.cs`**

Use the points as verified in Step 1. Write the sources you checked into the summary.

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Statecraft.Game.Map
{
    /// <summary>The map look's lines on the land: the Palestine Railways network in 1948 (Jaffa–Jerusalem, Lydda–Haifa via
    /// Tulkarm, Lydda–Gaza) and the Tel Aviv–Jerusalem road, as ribbons draped on the terrain. Route points were checked
    /// against the Survey of Palestine 1:250,000 sheets (1946) and the Wikipedia articles "Jaffa–Jerusalem railway",
    /// "Eastern Railway (Palestine)" and "Palestine Railways"; they are drawn at map scale, about 1 km accuracy.</summary>
    public static class MapLookLines
    {
        public readonly struct Route
        {
            public readonly string Id, Colour; public readonly float WidthM; public readonly (double lon, double lat)[] Points;
            public Route(string id, string colour, float widthM, params (double, double)[] points) { Id = id; Colour = colour; WidthM = widthM; Points = points; }
        }

        public const float StepM = 400f, LiftM = 25f, RailWidthM = 140f, RoadWidthM = 190f;

        public static readonly Route[] Routes =
        {
            new Route("rail-jaffa-jerusalem", "rail", RailWidthM, (34.765, 32.054), (34.882, 31.946), (34.872, 31.928), (34.860, 31.875),
                (34.920, 31.820), (34.980, 31.770), (35.040, 31.750), (35.080, 31.730), (35.135, 31.730), (35.225, 31.765)),
            new Route("rail-lydda-haifa", "rail", RailWidthM, (34.882, 31.946), (34.955, 32.095), (34.975, 32.190), (35.020, 32.315),
                (34.920, 32.440), (34.950, 32.520), (34.940, 32.690), (34.990, 32.820)),
            new Route("rail-lydda-gaza", "rail", RailWidthM, (34.882, 31.946), (34.740, 31.880), (34.660, 31.750), (34.580, 31.670), (34.460, 31.510)),
            new Route("road-telaviv-jerusalem", "road", RoadWidthM, (34.780, 32.080), (34.872, 31.928), (34.980, 31.840), (35.010, 31.810), (35.210, 31.780)),
        };

        /// <summary>A strip <paramref name="width"/> wide along <paramref name="path"/>, resampled every StepM, each edge vertex
        /// LiftM above the ground under it. Windings face up (Unity's front face is clockwise seen from above).</summary>
        public static Mesh Ribbon(IReadOnlyList<Vector3> path, float width, Func<Vector3, float> groundY)
        {
            var dense = Densify(path);
            var verts = new List<Vector3>(); var tris = new List<int>();
            for (int i = 0; i < dense.Count; i++)
            {
                var dir = dense[Mathf.Min(i + 1, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)]; dir.y = 0f; dir.Normalize();
                var side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
                Vector3 a = dense[i] - side, b = dense[i] + side;
                a.y = groundY(a) + LiftM; b.y = groundY(b) + LiftM;
                verts.Add(a); verts.Add(b);
                if (i == 0) continue;
                int n = verts.Count;   // previous pair n-4 (a), n-3 (b); this pair n-2 (a), n-1 (b)
                tris.Add(n - 3); tris.Add(n - 1); tris.Add(n - 2);
                tris.Add(n - 3); tris.Add(n - 2); tris.Add(n - 4);
            }
            var m = new Mesh { name = "look-ribbon" };
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static List<Vector3> Densify(IReadOnlyList<Vector3> path)
        {
            var dense = new List<Vector3> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(path[i - 1], path[i]) / StepM));
                for (int s = 1; s <= steps; s++) dense.Add(Vector3.Lerp(path[i - 1], path[i], (float)s / steps));
            }
            return dense;
        }

        public static GameObject Build(MapScene map, MapLookAssets assets, Transform root)
        {
            var go = new GameObject(MapLook.LinesToggle);
            go.transform.SetParent(root, false);
            var t = map.Terrain; var origin = t.transform.position;
            float Ground(Vector3 p) => t.SampleHeight(p) + origin.y;
            foreach (var route in Routes)
            {
                var path = new List<Vector3>();
                foreach (var (lon, lat) in route.Points) path.Add(map.Frame.ToWorld(lon, lat, 0f));
                var line = new GameObject(route.Id, typeof(MeshFilter), typeof(MeshRenderer));
                line.transform.SetParent(go.transform, false);
                line.GetComponent<MeshFilter>().sharedMesh = assets.Own(Ribbon(path, route.WidthM, Ground));
                var r = line.GetComponent<MeshRenderer>();
                r.sharedMaterial = assets.MaterialFor(route.Colour);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }
    }
}
```

Winding check: for a path along +z, `side` is −x, so `a` sits at +x (screen right seen from above) and `b` at −x. The
clockwise order bottom-left → top-left → top-right is `b₀ (n-3) → b₁ (n-1) → a₁ (n-2)`, and then `b₀ → a₁ → a₀ (n-4)`.
The test's `n.y > 0.5` confirms it.

- [ ] **Step 5: Wire it.** In `MapLook.Apply`, after the towns lines:

```csharp
            var lines = MapLookLines.Build(map, assets, root);
            Unregister.Add(VisualToggles.Register(LinesToggle, on => { if (lines != null) lines.SetActive(on); }));
```

- [ ] **Step 6: Run, confirm pass. Commit:** `feat(map): 1948 railway and the Jerusalem road for the map look`.
Revert the settings first.

---

### Task 6: Hook into the game and run the full suite

**Files:**
- Modify: `unity/Assets/Scripts/UI/GameRoot.cs:132`

- [ ] **Step 1: Add the one line** right after `BuildingPreview.Apply(map, BuildingPreview.Requested(), BuildingPreview.LoadFromResources);`:

```csharp
            MapLook.Apply(map, MapLook.Requested());
```

`GameRoot.cs` already uses `Statecraft.Game.Map` (it calls `BuildingPreview`), so no `using` is needed.

- [ ] **Step 2: Run the full EditMode suite** (no `-testFilter`):

```bash
git checkout -- unity/Assets/Settings unity/ProjectSettings/ProjectSettings.asset unity/ProjectSettings/QualitySettings.asset
"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -projectPath unity -buildTarget OSXUniversal -runTests -testPlatform EditMode -testResults "$PWD/unity-editmode.xml" -logFile unity-tests.log
grep -o 'result="[A-Za-z]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' unity-editmode.xml | head -1; grep -c "warning CS" unity-tests.log
```

Expected: all passed (501 before this plan, plus the new MapLookTests), and `0` warnings.
- `MapSceneTests.TheSeaBeyondTheEdgeMatchesTheTerrainSea` and the label-timing tests can fail under heavy load or with
  settings rewrites present. Revert the settings and re-run once before treating them as real.

- [ ] **Step 3: Run the repo checks**

```bash
uv run scripts/oss/check_repo.py && uv run --with pytest==9.1.1 --with pyyaml==6.0.3 --with requests==2.32.5 pytest scripts/oss/tests -q 2>&1 | tail -2
```

Expected: `OK` and all policy tests pass. No asset row is needed: the look adds no binary or model file.

- [ ] **Step 4: Commit:** `feat(map): apply the opt-in map look from GameRoot` (one line, plus nothing else).

---

### Task 7: WebGL build, goldens and budgets

- [ ] **Step 1: Build** (local only; the pending-rights marker is added automatically):

```bash
"$UNITY" -batchmode -CI false -GITHUB_ACTIONS false -TF_BUILD false -quit -projectPath unity -buildTarget WebGL -executeMethod Statecraft.Game.EditorTools.Build.Web -compression disabled -logFile "$PWD/unity-build-web.log"
du -sh unity/Builds/Web-disabled; ls unity/Builds/Web-disabled/PENDING-RIGHTS-PROTOTYPES.txt
```

Expected: the build succeeds. Its size is at most 15 MB above the wave-3 build (107 MB), and the marker file exists.

- [ ] **Step 2: Goldens and smoke in every combination.** Use the three full `--expect` values from `docs/determinism.md:34`.
Run them for each query:
  - `golden&smoke=5`
  - `golden&smoke=5&map-look`
  - `golden&smoke=5&map-look&building-preview&ui-preview=table`

```bash
uv run tools/web_check.py unity/Builds/Web-disabled --query "golden&smoke=5&map-look" <the three --expect values>
```

Expected: PASS on every query, which proves the simulation is untouched.

- [ ] **Step 3: Frame budget.** The look's toggles are registered, so the profile prices each one:

```bash
uv run tools/machine_lock.py --exclusive --wait-unity -- uv run tools/web_check.py unity/Builds/Web-disabled --query "vprofile&map-look" --expect "PROFILE visual done" --timeout 1800 --stall 120
```

Expected: `look-scatter` + `look-towns` + `look-lines` together cost at most 2.5 ms median at both poses.
- If they are over, set `MapLookScatter.DensityScale` to `0.6f`, or set `shadows: false` in the scatter `Emit`.
- Rebuild and re-measure. Record the numbers for the docs.

- [ ] **Step 4: Revert the settings rewrites** (`git checkout -- unity/Assets/Settings unity/ProjectSettings/ProjectSettings.asset unity/ProjectSettings/QualitySettings.asset`).

---

### Task 8: Captures, tuning and Tal's gate

- [ ] **Step 1: Serve the build** with the existing launch config (`statecraft-local-web`, port 8770).

- [ ] **Step 2: Capture matched pairs.** Use a 1920x1080 viewport, the same seed (`?golden`) and the same turn. Take each
pose twice, once with `&map-look` and once without:
  - the start view (Tel Aviv / Lod, Tal's screenshot);
  - Gameplay03 (34.86 E, 32.00 N, 60 km);
  - Diorama02 (35.05 E, 31.55 N, 250 km).

  Find how the pose shots are taken with `grep -n "shot" tools/art_checkpoint.sh`, and append `&map-look` to its query
  for the "after" set. Save the pairs to the ignored
  `asset-staging/ui-local/2026-10-12/map-look-review/` with a side-by-side `index.html`.

- [ ] **Step 3: Tune by eye, one change at a time, rebuilding between rounds** (at most three rounds):
  - `MapLook.Saturation`, `Contrast`, `Temperature`, `TiltRadiusScale`;
  - `MapLookAssets.Palette`;
  - `MapLookScatter.DensityScale` and the tree sizes.

  Keep the desert free of clipping: compare against `GradeTests.TheSunlitDesertDoesNotClip`'s threshold by eye. Re-run
  `MapLookTests` after each change.

- [ ] **Step 4: Send Tal the review page and stop.** Tal approves the captures, or lists changes, before Option A starts.

---

### Task 9: Document

**Files:**
- Modify: `docs/local-ui-preview.md` (new section at the top, under `## State`)

- [ ] **Step 1: Add the section**

```markdown
## Map look (Step 0), 12 October 2026 (local)

- **Switch:** `?map-look` (desktop `-map-look`). It composes with `?building-preview` and `?ui-preview=table`.
- **What it adds:** orchards, olive groves, cypress, palms, fields and rocks by biome; town blocks around cities, towns
  and arab towns; the 1948 railway and the Tel Aviv–Jerusalem road; a livelier grade with lighter tilt-shift.
- **Art:** all meshes are built in code (`MapLookShapes`). No asset files, downloads or licences are involved, so the
  look can ship publicly.
- **Original game footprint:** `TiltShift.RadiusScale` (2 lines, default 1) and one line in `GameRoot.cs`.
- **Verified:**
  - EditMode <N>/<N>, 0 warnings.
  - Goldens and smoke=5 PASS with and without the switch.
  - Look cost <x> ms at Gameplay03 and <y> ms at Diorama02.
  - Build <size> MB.
- **Review page:** `asset-staging/ui-local/2026-10-12/map-look-review/`.
```

Replace `<N>`, `<x>`, `<y>` and `<size>` with the measured numbers from Tasks 6–7.

- [ ] **Step 2: Commit:** `docs: map look (Step 0) state and verification`.
