#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using Statecraft.Game;
using Statecraft.Game.Boot;
using Statecraft.Game.Map;
using Statecraft.HexTiles;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>The hex-tile switch is opt-in and strict, as MapLook's is (the default game must not change).</summary>
public class HexTilesLookTests
{
    [TestCase("http://localhost/?hex-tiles", true)]
    [TestCase("http://localhost/?hex-tiles=1", true)]
    [TestCase("http://localhost/?map-look&hex-tiles", true)]
    [TestCase("http://localhost/?hex-tile", false)]
    [TestCase("http://localhost/?hex-tiles=0", false)]
    [TestCase("http://localhost/#hex-tiles", false)]
    [TestCase("http://localhost/?next=/p?hex-tiles", false)]
    [TestCase(null, false)]
    public void TheWebFlagIsStrict(string? url, bool expected) =>
        Assert.AreEqual(expected, HexTilesLook.Requested(url, Array.Empty<string>(), webgl: true));

    [Test]
    public void TheDesktopFlagIsTheExactArgument()
    {
        Assert.IsTrue(HexTilesLook.Requested(null, new[] { "Statecraft", "-hex-tiles" }, webgl: false));
        Assert.IsFalse(HexTilesLook.Requested(null, new[] { "-hex-tiles-x" }, webgl: false));
        Assert.IsFalse(HexTilesLook.Requested(null, Array.Empty<string>(), webgl: false));
    }

    /// <summary>Owner's review of #1120: removing the board, also right after rebuilding it, puts the map back as it was
    /// (every renderer's visibility, the terrain's heights and the dressing switches).</summary>
    [Test]
    public void RemovingTheBoardRestoresTheMapAfterARebuild()
    {
        EditorSceneManager.OpenScene("Assets/Map.unity", OpenSceneMode.Single);
        var map = Object.FindFirstObjectByType<MapScene>();
        map.Build(GameInputs.MapFromDisk(Paths.Content, Paths.StreamingMap));
        try
        {
            var renderers = map.GetComponentsInChildren<Renderer>(true);
            var before = renderers.Select(r => r.enabled).ToArray();
            var data = map.Terrain!.terrainData;
            var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            HexTilesLook.Apply(map, true);
            Assert.IsNotNull(map.transform.Find(HexTilesLook.RootName), "the board is up");
            HexTilesLook.Apply(map, true);    // a rebuild, as Begin() does on a language switch or a new game
            HexTilesLook.Apply(map, false);
            Assert.IsNull(map.transform.Find(HexTilesLook.RootName), "the board is gone");
            var changed = renderers.Where((r, i) => r != null && r.enabled != before[i]).Select(r => r.name).ToList();
            CollectionAssert.IsEmpty(changed, "renderers whose visibility the board left changed");
            var after = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            Assert.IsTrue(heights.Cast<float>().SequenceEqual(after.Cast<float>()), "the terrain's heights are restored");
        }
        finally { HexTilesLook.Apply(map, false); }
    }

    [Test]
    public void TheBoardShipsInResources() => Assert.That(HexBoard.Load(HexTilesLook.Board).Hexes.Count, Is.GreaterThan(0));
}
