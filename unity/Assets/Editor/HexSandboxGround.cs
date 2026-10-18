#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Statecraft.Game;
using Statecraft.Game.Map;
using UnityEditor;
using UnityEngine;

/// <summary>Sandbox-only textured ground: swaps the built map's in-memory terrain (MapBuilder makes its TerrainData at run
/// time; no asset is touched) from the Statecraft graph's flat biome colours to URP Terrain Lit with one Poly Haven CC0
/// layer per biome, blended by BiomeBlender's own weights so borders soften as the map's colours do. The Negev's non-desert
/// hexes take a loess layer, water a beach layer. Textures: &lt;Folder&gt;/&lt;id&gt;_diff_2k.jpg and &lt;id&gt;_nor_gl_2k.jpg,
/// git-ignored (asset-staging/kits/PROVENANCE.md). Apply returns the undo, or null when a texture is missing.</summary>
public static class HexSandboxGround
{
    public const string Folder = "Assets/Art/Settlements/Prototype/HexSandboxGround";
    public const int AlphamapResolution = 2048;   // ~79 m x 210 m texels over the 161 x 431 km terrain

    /// <summary>(layer, Poly Haven id, tile size m). Tiles of a kilometre or two read as field and scrub pattern at the
    /// 60 km Civ zoom without visible repeats at the 9 km close zoom.</summary>
    /// <summary>(layer, Poly Haven id, tile size m, tint). Tiles of several kilometres keep a photo texture's repeat from
    /// reading as a grid at the 60 km Civ zoom; the tint (TerrainLayer.diffuseRemapMax) pulls each photo toward a lusher
    /// version of its biome colour. "water" is a flat colour layer, painted where MapLookSurface sees sea or lake.</summary>
    public static readonly (string layer, string id, float tileM, Color tint)[] Layers =
    {
        ("water", "", 1000f, Color.white),
        ("beach", "aerial_beach_01", 2200f, new Color(1.10f, 1.05f, 0.92f)),
        ("coastal_plain", "aerial_grass_rock", 3400f, new Color(0.95f, 1.20f, 0.70f)),
        ("valley", "grass_ground", 2800f, new Color(1.00f, 1.20f, 0.75f)),
        ("hills", "aerial_ground_rock", 4000f, new Color(0.95f, 1.12f, 0.78f)),
        ("mountains", "rocky_terrain_02", 6000f, new Color(1.05f, 1.05f, 0.90f)),
        ("desert", "aerial_sand", 4500f, new Color(1.25f, 1.08f, 0.85f)),
        // dirt_aerial_02's tracks tiled into a diagonal grid at the Civ zoom; the sand photo, larger and browner, reads as loess.
        ("loess", "aerial_sand", 7000f, new Color(1.10f, 0.98f, 0.72f)),
    };

    static float[,,]? cached;

    public static string LayerOf(HexJson h) =>
        !h.land ? "beach" : h.biome == "desert" ? "desert"
        : HexSandboxPlanting.RegionOf(h.lat) == HexSandboxPlanting.Region.Negev ? "loess"
        : Layers.Any(l => l.layer == h.biome) ? h.biome! : "hills";

    public static Action? Apply(MapScene map)
    {
        var missing = Layers.Where(l => l.id != "").SelectMany(l => new[] { $"{l.id}_diff_2k.jpg", $"{l.id}_nor_gl_2k.jpg" })
            .Where(f => !File.Exists(Path.Combine(Paths.RepoRoot, "unity", Folder, f))).ToList();
        if (missing.Count > 0) { Debug.LogWarning("HEX SANDBOX GROUND missing: " + string.Join(", ", missing)); return null; }
        var t = map.Terrain; var data = t.terrainData;
        var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit") ?? throw new InvalidOperationException("URP Terrain/Lit not found");
        var oldMaterial = t.materialTemplate; var oldLayers = data.terrainLayers; int oldRes = data.alphamapResolution;
        var oldAlpha = data.GetAlphamaps(0, 0, oldRes, oldRes);
        var made = new List<UnityEngine.Object>();
        var water = new Texture2D(4, 4, TextureFormat.RGB24, true) { name = "sandbox-water" };
        water.SetPixels(Enumerable.Repeat(MapBuilder.BiomeColours["sea"], 16).ToArray()); water.Apply();
        made.Add(water);
        var layers = Layers.Select(l =>
        {
            var layer = new TerrainLayer
            {
                name = "sandbox-" + l.layer, diffuseTexture = l.id == "" ? water : Tex($"{l.id}_diff_2k.jpg", false),
                normalMapTexture = l.id == "" ? null : Tex($"{l.id}_nor_gl_2k.jpg", true),
                tileSize = new Vector2(l.tileM, l.tileM), normalScale = 0.6f, smoothness = 0f, metallic = 0f,
                diffuseRemapMin = Vector4.zero, diffuseRemapMax = new Vector4(l.tint.r, l.tint.g, l.tint.b, 1f),
            };
            made.Add(layer); return layer;
        }).ToArray();
        var material = new Material(shader) { name = "sandbox-terrain" };
        material.EnableKeyword("_NORMALMAP");
        made.Add(material);
        data.alphamapResolution = AlphamapResolution;
        data.terrainLayers = layers;
        data.SetAlphamaps(0, 0, cached ??= Weights(map));
        t.materialTemplate = material;
        return () =>
        {
            t.materialTemplate = oldMaterial;
            data.terrainLayers = oldLayers; data.alphamapResolution = oldRes; data.SetAlphamaps(0, 0, oldAlpha);
            foreach (var o in made) UnityEngine.Object.DestroyImmediate(o);
        };
    }

    static float[,,] Weights(MapScene map)
    {
        var hexes = map.Hexes.hexes.OrderBy(h => h.q).ThenBy(h => h.r).ToArray();
        var names = Layers.Select(l => l.layer).ToList();
        var centres = hexes.Select(h => { var p = map.Frame.ToWorld(h.lon, h.lat, 0f); return new Vector2(p.x, p.z); }).ToArray();
        var blender = new BiomeBlender(centres, hexes.Select(h => names.IndexOf(LayerOf(h))).ToArray(), map.Hexes.hex_size_km * 1000f, names.Count);
        var t = map.Terrain; var size = t.terrainData.size; var origin = t.transform.position;
        int n = AlphamapResolution;
        var alpha = new float[n, n, names.Count];
        var w = new float[names.Count];
        int waterLayer = names.IndexOf("water"), beachLayer = names.IndexOf("beach");
        using var surface = new MapLookSurface(map);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector3(origin.x + size.x * x / (n - 1f), 0f, origin.z + size.z * y / (n - 1f));
                if (surface.IsWater(p)) { alpha[y, x, waterLayer] = 1f; continue; }
                blender.Weights(new Vector2(p.x, p.z), w);
                w[waterLayer] = 0f;   // a sea hex's weight on dry land is shore: sand, not water
                float sum = 0f; for (int l = 0; l < w.Length; l++) sum += w[l];
                for (int l = 0; l < w.Length; l++) alpha[y, x, l] = sum > 0f ? w[l] / sum : l == beachLayer ? 1f : 0f;
            }
        return alpha;
    }

    static Texture2D Tex(string file, bool normal)
    {
        var path = $"{Folder}/{file}";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type || ti.maxTextureSize != 2048) { ti.textureType = type; ti.maxTextureSize = 2048; ti.mipmapEnabled = true; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
