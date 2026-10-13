#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>The map-look switch owns only presentation, restores the preceding picture and releases every resource.</summary>
public class MapLookTests
{
    [SetUp] public void FreshScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    [TearDown]
    public void DiscardScene()
    {
        var map = Object.FindFirstObjectByType<MapScene>();
        if (map != null) MapLook.Apply(map, false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        TiltShift.Reset();
    }

    static Material Load(string name)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + name + ".mat");
        Assert.IsNotNull(material, name);
        return material;
    }

    static MapScene BuiltMap()
    {
        new GameObject("TestMainCamera") { tag = "MainCamera" }.AddComponent<Camera>();
        var map = new GameObject("MapScene").AddComponent<MapScene>();
        map.Configure(Load("Terrain"), Load("SettlementMarker"), Load("HexSelection"), Load("Sea"), Load("BorderLand"));
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
        Assert.AreEqual(expected, MapLook.Requested(url, Array.Empty<string>(), webgl: true));

    [Test]
    public void TheDesktopFlagIsTheExactArgument()
    {
        Assert.IsTrue(MapLook.Requested(null, new[] { "Statecraft", "-map-look" }, webgl: false));
        Assert.IsFalse(MapLook.Requested(null, new[] { "-map-looks" }, webgl: false));
    }

    [Test]
    public void WithoutTheSwitchTheSceneAndPictureAreUntouched()
    {
        var map = BuiltMap();
        var grade = Grade();
        grade.saturation.value = 7f;
        grade.saturation.overrideState = false;
        TiltShift.RadiusScale = 0.9f;
        var children = map.transform.childCount;
        MapLook.Apply(map, false);
        Assert.AreEqual(children, map.transform.childCount);
        Assert.IsNull(map.transform.Find(MapLook.RootName));
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-", StringComparison.Ordinal)));
        Assert.AreEqual(7f, grade.saturation.value);
        Assert.IsFalse(grade.saturation.overrideState);
        Assert.AreEqual(0.9f, TiltShift.RadiusScale);
    }

    [Test]
    public void ThePictureCanBeToggledAndRestoresValuesAndOverrideState()
    {
        var map = BuiltMap();
        var grade = Grade();
        grade.saturation.value = 7f; grade.saturation.overrideState = false;
        grade.contrast.value = 11f; grade.postExposure.value = -0.1f;
        TiltShift.RadiusScale = 0.8f;
        MapLook.Apply(map, true);
        Assert.AreEqual(MapLook.Saturation, grade.saturation.value);
        Assert.AreEqual(MapLook.TiltRadiusScale, TiltShift.RadiusScale);
        VisualToggles.Set(MapLook.GradeToggle, false);
        Assert.AreEqual(7f, grade.saturation.value);
        Assert.IsFalse(grade.saturation.overrideState);
        Assert.AreEqual(0.8f, TiltShift.RadiusScale);
        VisualToggles.Set(MapLook.GradeToggle, true);
        Assert.AreEqual(MapLook.Saturation, grade.saturation.value);
        MapLook.Apply(map, false);
        Assert.AreEqual(7f, grade.saturation.value);
        Assert.AreEqual(11f, grade.contrast.value);
        Assert.AreEqual(-0.1f, grade.postExposure.value);
        Assert.IsFalse(grade.saturation.overrideState);
        Assert.AreEqual(0.8f, TiltShift.RadiusScale);
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-", StringComparison.Ordinal)));
    }

    [Test]
    public void TheRadiusScaleChangesOnlyTheBlurRadiusAndResetRestoresIt()
    {
        var plain = TiltShift.Params(0.1f, 30000f);
        TiltShift.RadiusScale = 0.5f;
        var scaled = TiltShift.Params(0.1f, 30000f);
        Assert.AreEqual(plain.w * 0.5f, scaled.w, 1e-7f);
        Assert.AreEqual(new Vector3(plain.x, plain.y, plain.z), new Vector3(scaled.x, scaled.y, scaled.z));
        TiltShift.Reset();
        Assert.AreEqual(1f, TiltShift.RadiusScale);
    }

    [Test]
    public void EveryShapeStandsOnTheOriginOneUnitTallWithinAUnitFootprint()
    {
        foreach (var name in MapLookShapes.All)
        {
            var parts = MapLookShapes.Build(name);
            try
            {
                Assert.IsNotEmpty(parts, name);
                var bounds = parts[0].Mesh.bounds;
                foreach (var part in parts) bounds.Encapsulate(part.Mesh.bounds);
                Assert.AreEqual(0f, bounds.min.y, 1e-4f, name);
                Assert.AreEqual(1f, bounds.max.y, 1e-4f, name);
                Assert.LessOrEqual(Mathf.Max(bounds.size.x, bounds.size.z), 1.01f, name);
                foreach (var part in parts) Assert.IsTrue(MapLookAssets.Palette.ContainsKey(part.Colour), name);
            }
            finally { foreach (var part in parts) Object.DestroyImmediate(part.Mesh); }
        }
    }

    [Test]
    public void SideFacesPointOutwardAndCapsFaceAwayFromTheShape()
    {
        foreach (var name in new[] { MapLookShapes.Olive, MapLookShapes.BlockPlaster })
        {
            var parts = MapLookShapes.Build(name);
            try
            {
                var mesh = parts[parts.Length - 1].Mesh;
                var v = mesh.vertices; var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).normalized;
                    if (Mathf.Abs(normal.y) > 0.9f)
                    {
                        Assert.AreEqual(v[t[i]].y > 0.5f, normal.y > 0f, name);
                        continue;
                    }
                    var centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Assert.Greater(Vector3.Dot(normal, new Vector3(centre.x, 0f, centre.z)), 0f, name);
                }
            }
            finally { foreach (var part in parts) Object.DestroyImmediate(part.Mesh); }
        }
    }

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
    public void TheScatterIsDeterministicAndRespectsSeaAndSettlementFootprints()
    {
        var sea = TestFrame.ToWorld(34.75, 32.0, 0f).x;
        float Ground(Vector3 p) => p.x < sea ? -50f : 100f;
        var town = TestFrame.ToWorld(34.80, 32.00, 0f);
        var avoid = new[] { (town, 2000f) };
        var hexes = TestHexes();
        var a = MapLookScatter.Plan(hexes, TestFrame, Ground, _ => 0f, 0f, avoid);
        Array.Reverse(hexes.hexes);
        var b = MapLookScatter.Plan(hexes, TestFrame, Ground, _ => 0f, 0f, avoid);
        Assert.Greater(a.Count, 50);
        CollectionAssert.AreEqual(a.Select(p => (p.At, p.Shape, p.Scale, p.Yaw)), b.Select(p => (p.At, p.Shape, p.Scale, p.Yaw)));
        foreach (var p in a)
        {
            Assert.GreaterOrEqual(p.At.x, sea - 1f);
            float footprint = Mathf.Max(p.Scale.x, p.Scale.z) * 0.5f;
            Assert.GreaterOrEqual(new Vector2(p.At.x - town.x, p.At.z - town.z).magnitude, 2000f + footprint);
        }
        Assert.IsTrue(a.Any(p => p.Shape == MapLookShapes.Citrus));
        Assert.IsTrue(a.Any(p => p.Shape == MapLookShapes.Olive));
        Assert.IsTrue(a.Any(p => p.Shape == MapLookShapes.FieldWheat));
    }

    [Test]
    public void TreesSkipSteepGroundAndFieldsSkipUnevenGround()
    {
        float Ground(Vector3 p) => Mathf.Floor(p.x / 300f) * 200f;
        var steep = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, _ => 40f, -1000f, Array.Empty<(Vector3, float)>());
        Assert.IsEmpty(steep.Where(p => p.Shape != MapLookShapes.FieldWheat && p.Shape != MapLookShapes.FieldGreen));
        var uneven = MapLookScatter.Plan(TestHexes(), TestFrame, Ground, _ => 0f, -1000f, Array.Empty<(Vector3, float)>());
        Assert.IsEmpty(uneven.Where(p => p.Shape == MapLookShapes.FieldWheat || p.Shape == MapLookShapes.FieldGreen));
    }

    [Test]
    public void LowQualityReducesTheScatterWithoutChangingTheDefaultMap()
    {
        var full = MapLookScatter.Plan(TestHexes(), TestFrame, _ => 100f, _ => 0f, 0f, Array.Empty<(Vector3, float)>());
        var low = MapLookScatter.Plan(TestHexes(), TestFrame, _ => 100f, _ => 0f, 0f, Array.Empty<(Vector3, float)>(), density: 0.6f);
        Assert.Greater(low.Count, 0);
        Assert.Less(low.Count, full.Count);
    }

    [Test]
    public void BelowSeaLevelDryLandIsDressedWhileLakesRemainEmpty()
    {
        var dry = MapLookScatter.Plan(TestHexes(), TestFrame, _ => -300f, _ => 0f, 0f, Array.Empty<(Vector3, float)>(), isWater: _ => false);
        Assert.Greater(dry.Count, 50, "below-sea dry land was mistaken for water");
        var wet = MapLookScatter.Plan(TestHexes(), TestFrame, _ => -300f, _ => 0f, 0f, Array.Empty<(Vector3, float)>(), isWater: _ => true);
        Assert.IsEmpty(wet);
        var town = new Settlement { Id = "depression_town", Name = "Depression town", Type = "town", Lon = 34.90, Lat = 32.00 };
        var blocks = MapLookTowns.Plan(new[] { town }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => -300f, _ => 0f, 0f, isWater: _ => false);
        Assert.IsNotEmpty(blocks);
        Assert.IsEmpty(MapLookTowns.Plan(new[] { town }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => -300f, _ => 0f, 0f, isWater: _ => true));
    }

    [Test]
    public void HillsIncludeLowStoneWallsAndSeaDressingNeedsNoPrototypeAssets()
    {
        var plants = MapLookScatter.Plan(TestHexes(), TestFrame, _ => 100f, _ => 0f, 0f, Array.Empty<(Vector3, float)>());
        var walls = plants.Where(p => p.Shape == MapLookShapes.Wall).ToArray();
        Assert.IsNotEmpty(walls);
        Assert.IsTrue(walls.All(p => p.Scale.y < p.Scale.x * 0.3f));
        foreach (var shape in new[] { MapLookShapes.FishingBoat, MapLookShapes.Steamer })
        {
            var parts = MapLookShapes.Build(shape);
            try { Assert.Greater(parts.Length, 1); }
            finally { foreach (var part in parts) Object.DestroyImmediate(part.Mesh); }
        }
    }

    [Test]
    public void TownBlocksRingTheMarkerAndStayUnderItsLabel()
    {
        var s = new Settlement { Id = "tel_aviv", Name = "Tel Aviv", Type = "city", Lon = 34.78, Lat = 32.08 };
        var centre = TestFrame.ToWorld(s.Lon, s.Lat, 0f);
        var blocks = MapLookTowns.Plan(new[] { s }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => 50f, _ => 0f, 0f);
        Assert.Greater(blocks.Count, 20);
        foreach (var block in blocks)
        {
            Assert.GreaterOrEqual(new Vector2(block.At.x - centre.x, block.At.z - centre.z).magnitude, 1890f);
            Assert.LessOrEqual(block.At.y + block.Scale.y, 1041f);
        }
        CollectionAssert.AreEqual(blocks.Select(b => b.At), MapLookTowns.Plan(new[] { s }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), _ => 50f, _ => 0f, 0f).Select(b => b.At));
    }

    [Test]
    public void TownBlocksStayUnderTheSettlementLabelOnRisingTerrain()
    {
        var s = new Settlement { Id = "slope_town", Name = "Slope town", Type = "town", Lon = 34.90, Lat = 32.00 };
        var centre = TestFrame.ToWorld(s.Lon, s.Lat, 0f);
        float Ground(Vector3 p) => 100f + Mathf.Abs(p.x - centre.x) * 0.6f;
        var blocks = MapLookTowns.Plan(new[] { s }, (lon, lat) => TestFrame.ToWorld(lon, lat, 0f), Ground, _ => 10f, 0f);
        Assert.IsNotEmpty(blocks);
        foreach (var block in blocks) Assert.LessOrEqual(block.At.y + block.Scale.y, 1200f, "roof obscures the settlement's label anchor");
    }

    [Test]
    public void ARibbonFacesUpAndFollowsTheGroundEvenWithRepeatedPoints()
    {
        var path = new[] { Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 2000f), new Vector3(1000f, 0f, 3000f) };
        var mesh = MapLookLines.Ribbon(path, 140f, p => p.z * 0.01f);
        try
        {
            Assert.Greater(mesh.vertexCount, 10);
            foreach (var normal in mesh.normals) Assert.Greater(normal.y, 0.5f);
            foreach (var v in mesh.vertices) Assert.AreEqual(v.z * 0.01f + MapLookLines.LiftM, v.y, 0.01f);
        }
        finally { Object.DestroyImmediate(mesh); }
        Assert.Throws<ArgumentException>(() => MapLookLines.Ribbon(Array.Empty<Vector3>(), 140f, _ => 0f));
        foreach (var route in MapLookLines.Routes)
        {
            Assert.GreaterOrEqual(route.Points.Length, 2, route.Id);
            Assert.IsNotEmpty(route.SourceUrl, route.Id);
            Assert.IsTrue(MapLookAssets.Palette.ContainsKey(route.Colour), route.Id);
        }
    }

    [Test]
    public void TheLookOwnsLayersResourcesAndTogglesAcrossReapplyAndDestruction()
    {
        var map = BuiltMap();
        MapLook.Apply(map, true);
        Assert.IsEmpty(Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t.name == "look-coast-readback"), "coast readback copy leaked after placement");
        var root = map.transform.Find(MapLook.RootName);
        Assert.IsNotNull(root);
        Assert.Greater(root.GetComponentsInChildren<MeshRenderer>().Length, 10);
        Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
        var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
        var material = root.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        VisualToggles.Set(MapLook.ScatterToggle, false);
        Assert.IsFalse(root.Find(MapLook.ScatterToggle).gameObject.activeSelf);
        MapLook.Apply(map, true);
        Assert.IsTrue(mesh == null, "reapply leaked a combined mesh");
        Assert.IsTrue(material == null, "reapply leaked a palette material");
        Assert.AreEqual(1, map.transform.Cast<Transform>().Count(t => t.name == MapLook.RootName));
        Object.DestroyImmediate(map.transform.Find(MapLook.RootName).gameObject);
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-", StringComparison.Ordinal)));
        Assert.AreEqual(MapGrade.Saturation, Grade().saturation.value);
        Assert.AreEqual(1f, TiltShift.RadiusScale);
    }

    [Test]
    public void RebuildingTheMapRemovesLookTogglesBeforeTheNextPictureIsCreated()
    {
        var map = BuiltMap();
        MapLook.Apply(map, true);
        map.Build();
        Assert.IsNull(map.transform.Find(MapLook.RootName));
        Assert.IsFalse(VisualToggles.Names.Any(n => n.StartsWith("look-", StringComparison.Ordinal)));
        Assert.AreEqual(MapGrade.Saturation, Grade().saturation.value);
        Assert.AreEqual(1f, TiltShift.RadiusScale);
    }
}
