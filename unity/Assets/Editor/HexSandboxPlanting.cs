#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Sandbox-only planting by region and biome, in species rather than shapes, so a species' look can be swapped
/// (code shape now, a realistic model later) without touching the rules. Region by latitude: North (Galilee, the valleys)
/// from 32.45, the Negev below 31.55, the Centre between; a "desert" hex is desert in any region. Pure Plan seeded by
/// (q, r) like MapLookScatter, which stays untouched; promote rules there only after Tal approves the captures.
/// A species draws its model from Resources/HexSandbox/&lt;species&gt; when one is present (ignored Prototype folder,
/// normalised to the species' height, base on the ground), else its stand-in code shape tinted to the species' colour.</summary>
public static class HexSandboxPlanting
{
    public const string ModelsFolder = "HexSandbox";
    public enum Region { North, Centre, Negev }

    /// <summary>Stand-in: a MapLookShapes shape, its height and width in diorama metres, foliage tint (null keeps the
    /// palette), and whether it casts shadows (trees yes; fields, scrub and rocks no, as MapLookScatter).</summary>
    public sealed class Species
    {
        public readonly string Name, Shape; public readonly float HeightM, WidthM; public readonly Color? Tint; public readonly bool Shadows;
        /// <summary>A model keeps its own height-to-width ratio (rock scans); else it is stretched to HeightM (trees).</summary>
        public bool KeepAspect => Shape == MapLookShapes.Rock || Name.StartsWith("kit_", StringComparison.Ordinal);
        public Species(string name, string shape, float heightM, float widthM, Color? tint, bool shadows)
        { Name = name; Shape = shape; HeightM = heightM; WidthM = widthM; Tint = tint; Shadows = shadows; }
    }

    public static readonly IReadOnlyDictionary<string, Species> All = new[]
    {
        new Species("tabor_oak", MapLookShapes.Olive, 480f, 600f, new Color(0.20f, 0.33f, 0.16f), true),
        new Species("aleppo_pine", MapLookShapes.Cypress, 620f, 460f, new Color(0.17f, 0.29f, 0.17f), true),
        new Species("cypress", MapLookShapes.Cypress, 640f, 240f, null, true),
        new Species("olive", MapLookShapes.Olive, 380f, 420f, null, true),
        new Species("carob", MapLookShapes.Citrus, 400f, 460f, new Color(0.16f, 0.30f, 0.14f), true),
        new Species("citrus", MapLookShapes.Citrus, 320f, 320f, null, true),
        new Species("date_palm", MapLookShapes.Palm, 720f, 720f, null, true),
        new Species("acacia", MapLookShapes.Olive, 280f, 560f, new Color(0.52f, 0.52f, 0.30f), true),
        new Species("retama_scrub", MapLookShapes.Rock, 110f, 220f, new Color(0.58f, 0.60f, 0.45f), false),
        new Species("garrigue", MapLookShapes.Rock, 100f, 240f, new Color(0.40f, 0.46f, 0.30f), false),
        new Species("limestone", MapLookShapes.Rock, 240f, 260f, null, false),
        new Species("desert_rock", MapLookShapes.Rock, 280f, 380f, new Color(0.66f, 0.52f, 0.38f), false),
        new Species("terrace_wall", MapLookShapes.Wall, 70f, 500f, null, false),
        new Species("wheat", MapLookShapes.FieldWheat, 0f, 0f, null, false),
        new Species("green_field", MapLookShapes.FieldGreen, 0f, 0f, null, false),
        new Species("barley_dry", MapLookShapes.FieldWheat, 0f, 0f, new Color(0.80f, 0.70f, 0.46f), false),
        new Species("ploughed", MapLookShapes.FieldWheat, 0f, 0f, new Color(0.55f, 0.42f, 0.30f), false),
        new Species("vegetable_rows", MapLookShapes.FieldGreen, 0f, 0f, new Color(0.40f, 0.55f, 0.25f), false),
        // City pieces, placed by HexSandboxCity with their own matrices (TryPlant is not used for them).
        new Species("block_plaster", MapLookShapes.BlockPlaster, 1f, 1f, new Color(0.94f, 0.92f, 0.86f), true),
        new Species("block_stone", MapLookShapes.BlockStone, 1f, 1f, new Color(0.86f, 0.77f, 0.60f), true),
        new Species("block_ochre", MapLookShapes.BlockPlaster, 1f, 1f, new Color(0.90f, 0.78f, 0.58f), true),
        new Species("roof_terracotta", MapLookShapes.BlockPlaster, 1f, 1f, new Color(0.72f, 0.36f, 0.24f), true),
        new Species("street", MapLookShapes.FieldGreen, 0f, 0f, new Color(0.62f, 0.58f, 0.50f), false),
        new Species("wall_old_city", MapLookShapes.Wall, 1f, 1f, new Color(0.84f, 0.76f, 0.60f), true),
        new Species("kit_white_city", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_jaffa", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_haifa", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_jerusalem", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_negev", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_moshava", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_kibbutz", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_stone_village", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("kit_port", MapLookShapes.BlockPlaster, 1f, 1f, null, true),
        new Species("tent_bedouin", MapLookShapes.BlockPlaster, 1f, 1f, new Color(0.22f, 0.19f, 0.17f), true),
    }.ToDictionary(s => s.Name, StringComparer.Ordinal);

    // (species, groups per hex, per group, spacing). Up to 9 is a grid (a grove, an orchard row, 3 x 3); more than 9 is a
    // clump: a wood of that many trees scattered in a disc whose size follows spacing, so woods read as one canopy.
    static readonly Dictionary<(Region, string), (string sp, int groups, int per, float spacing)[]> Rules = new()
    {
        // Galilee: oak-terebinth woods and pine forest over the hills, olive terraces lower down; fields in the valleys.
        [(Region.North, "hills")] = new[] { ("tabor_oak", 5, 32, 420f), ("aleppo_pine", 3, 36, 380f), ("olive", 5, 9, 460f), ("garrigue", 10, 6, 320f), ("terrace_wall", 3, 3, 520f), ("limestone", 4, 2, 420f) },
        [(Region.North, "mountains")] = new[] { ("tabor_oak", 5, 36, 420f), ("aleppo_pine", 4, 40, 380f), ("cypress", 2, 9, 420f), ("garrigue", 12, 6, 320f), ("limestone", 6, 2, 420f) },
        [(Region.North, "valley")] = new[] { ("wheat", 7, 1, 0f), ("green_field", 7, 1, 0f), ("olive", 3, 9, 460f), ("tabor_oak", 2, 14, 420f), ("cypress", 2, 3, 340f) },
        [(Region.North, "coastal_plain")] = new[] { ("green_field", 6, 1, 0f), ("wheat", 5, 1, 0f), ("citrus", 4, 9, 420f), ("date_palm", 2, 3, 560f), ("tabor_oak", 2, 14, 420f) },
        // Centre: citrus groves and palms on the coast; olive terraces and JNF pine forest on the Judean and Samarian hills.
        [(Region.Centre, "coastal_plain")] = new[] { ("citrus", 10, 9, 420f), ("green_field", 4, 1, 0f), ("wheat", 3, 1, 0f), ("date_palm", 3, 5, 560f), ("carob", 2, 12, 440f), ("olive", 2, 9, 460f) },
        [(Region.Centre, "valley")] = new[] { ("wheat", 6, 1, 0f), ("green_field", 5, 1, 0f), ("olive", 4, 9, 460f), ("carob", 2, 12, 440f) },
        [(Region.Centre, "hills")] = new[] { ("olive", 12, 9, 460f), ("aleppo_pine", 3, 34, 380f), ("carob", 2, 12, 440f), ("garrigue", 8, 6, 320f), ("terrace_wall", 5, 3, 520f), ("limestone", 5, 2, 420f) },
        [(Region.Centre, "mountains")] = new[] { ("olive", 8, 9, 460f), ("aleppo_pine", 4, 36, 380f), ("cypress", 2, 9, 420f), ("garrigue", 10, 6, 320f), ("terrace_wall", 4, 3, 520f), ("limestone", 6, 2, 420f) },
        // Negev: loess barley and scrub in the north, acacia along wadis, open scrub and rock on the plateau.
        [(Region.Negev, "coastal_plain")] = new[] { ("barley_dry", 5, 1, 0f), ("wheat", 2, 1, 0f), ("retama_scrub", 10, 6, 360f), ("acacia", 3, 3, 700f), ("date_palm", 1, 5, 560f) },
        [(Region.Negev, "valley")] = new[] { ("barley_dry", 5, 1, 0f), ("retama_scrub", 10, 6, 360f), ("acacia", 4, 3, 700f) },
        [(Region.Negev, "hills")] = new[] { ("barley_dry", 3, 1, 0f), ("retama_scrub", 12, 6, 360f), ("acacia", 4, 2, 700f), ("desert_rock", 5, 3, 480f) },
        [(Region.Negev, "mountains")] = new[] { ("retama_scrub", 8, 6, 380f), ("desert_rock", 8, 3, 480f), ("acacia", 2, 2, 700f) },
    };
    static readonly (string sp, int groups, int per, float spacing)[] Desert =
        { ("retama_scrub", 10, 6, 400f), ("acacia", 4, 2, 750f), ("desert_rock", 7, 3, 500f) };

    public static Region RegionOf(double lat) => lat >= 32.45 ? Region.North : lat < 31.55 ? Region.Negev : Region.Centre;

    public static IReadOnlyList<(string sp, int groups, int per, float spacing)> RulesFor(HexJson h) =>
        h.biome == "desert" ? Desert : Rules.TryGetValue((RegionOf(h.lat), h.biome ?? ""), out var r) ? r : Array.Empty<(string, int, int, float)>();

    public readonly struct Plant
    {
        public readonly string Species; public readonly Matrix4x4 Matrix;
        public Plant(string species, Matrix4x4 matrix) { Species = species; Matrix = matrix; }
    }

    public static List<Plant> Plan(HexFile patch, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
        IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool> isWater)
    {
        var plants = new List<Plant>();
        float hexM = patch.hex_size_km * 1000f;
        foreach (var h in patch.hexes.OrderBy(h => h.q).ThenBy(h => h.r))
        {
            if (!h.land) continue;
            var rng = new System.Random(MapLookScatter.Seed ^ (h.q * 73856093) ^ (h.r * 19349663));
            var centre = frame.ToWorld(h.lon, h.lat, 0f);
            foreach (var (sp, groups, per, spacing) in RulesFor(h))
            {
                var species = All[sp];
                for (int g = 0; g < groups; g++)
                {
                    // Up to the hex's corners (1.0 x size), so neighbouring hexes' plantings meet instead of leaving rings.
                    var origin = centre + Disc(rng, hexM);
                    var row = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f);
                    for (int i = 0; i < per; i++)
                    {
                        var offset = per == 1 ? Vector3.zero
                            : per > 9 ? Disc(rng, spacing * 0.6f * Mathf.Sqrt(per))
                            : row * new Vector3((i % 3 - 1) * spacing, 0f, (i / 3 - 1) * spacing) + Disc(rng, spacing * 0.3f);
                        if (TryPlant(species, origin + offset, rng, frame, groundY, slopeDeg, avoid, isWater, out var m)) plants.Add(new Plant(sp, m));
                    }
                }
            }
        }
        return plants;
    }

    /// <summary>A herd of one of Tal's local Tripo animals (Resources/SettlementPrototypes), sized by its largest dimension.</summary>
    public readonly struct Animal
    {
        public readonly string Prefab; public readonly Vector3 At; public readonly float Yaw, SizeM;
        public Animal(string prefab, Vector3 at, float yaw, float sizeM) { Prefab = prefab; At = at; Yaw = yaw; SizeM = sizeM; }
    }

    // (prefab, chance per land hex, head count min..max, largest dimension m, max slope): sheep and a donkey on the north and
    // centre hills, camel strings and ibex in the Negev, now and then a hyena.
    static readonly Dictionary<Region, (string prefab, float chance, int min, int max, float sizeM, float slope)[]> Herds = new()
    {
        [Region.North] = new[] { ("figure-sheep-idle-1948-v1", 0.45f, 8, 14, 260f, 25f), ("figure-donkey-1948-v1", 0.2f, 1, 2, 320f, 25f) },
        [Region.Centre] = new[] { ("figure-sheep-idle-1948-v1", 0.35f, 6, 12, 260f, 25f), ("figure-donkey-1948-v1", 0.2f, 1, 2, 320f, 25f) },
        [Region.Negev] = new[] { ("figure-dromedary-camel-1948-v1", 0.3f, 3, 6, 480f, 20f), ("figure-nubian-ibex-1948-v1", 0.25f, 3, 5, 280f, 40f),
            ("figure-sheep-idle-1948-v1", 0.15f, 6, 10, 260f, 20f), ("figure-striped-hyena-1948-v1", 0.06f, 1, 1, 260f, 30f) },
    };

    public static List<Animal> PlanHerds(HexFile patch, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
        IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool> isWater)
    {
        var animals = new List<Animal>();
        float hexM = patch.hex_size_km * 1000f;
        foreach (var h in patch.hexes.OrderBy(h => h.q).ThenBy(h => h.r))
        {
            if (!h.land) continue;
            var rng = new System.Random(MapLookScatter.Seed * 31 ^ (h.q * 73856093) ^ (h.r * 19349663));
            var centre = frame.ToWorld(h.lon, h.lat, 0f);
            foreach (var (prefab, chance, min, max, sizeM, slope) in Herds[RegionOf(h.lat)])
            {
                if (rng.NextDouble() >= chance) continue;
                var origin = centre + Disc(rng, 0.8f * hexM);
                float heading = (float)rng.NextDouble() * 360f;
                int count = rng.Next(min, max + 1);
                for (int i = 0; i < count; i++)
                {
                    var p = origin + Disc(rng, sizeM * 1.2f * Mathf.Sqrt(count));
                    if (isWater(p) || slopeDeg(p) > slope) continue;
                    if (avoid.Any(a => new Vector2(p.x - a.at.x, p.z - a.at.z).sqrMagnitude < a.radius * a.radius)) continue;
                    animals.Add(new Animal(prefab, new Vector3(p.x, groundY(p), p.z), heading + (float)(rng.NextDouble() - 0.5) * 70f, sizeM * (0.85f + 0.3f * (float)rng.NextDouble())));
                }
            }
        }
        return animals;
    }

    /// <summary>Instantiates each animal under <paramref name="parent"/>, scaled to its size and posed at its idle's first
    /// frame; a prefab that is not imported is skipped and reported once.</summary>
    public static int PlaceHerds(Transform parent, IEnumerable<Animal> animals, ISet<string> missing)
    {
        int placed = 0;
        var prefabs = new Dictionary<string, (GameObject? prefab, float largest)>(StringComparer.Ordinal);
        foreach (var a in animals)
        {
            if (!prefabs.TryGetValue(a.Prefab, out var entry))
            {
                var prefab = Resources.Load<GameObject>("SettlementPrototypes/" + a.Prefab);
                float largest = 1f;
                if (prefab != null)
                {
                    var probe = Object.Instantiate(prefab);
                    var b = probe.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                    largest = Mathf.Max(b.size.x, b.size.y, b.size.z, 1e-4f);
                    Object.DestroyImmediate(probe);
                }
                else missing.Add(a.Prefab);
                prefabs[a.Prefab] = entry = (prefab, largest);
            }
            if (entry.prefab == null) continue;
            var go = Object.Instantiate(entry.prefab, parent);
            go.transform.SetPositionAndRotation(a.At, Quaternion.Euler(0f, a.Yaw, 0f));
            go.transform.localScale = Vector3.one * (a.SizeM / entry.largest);
            foreach (var anim in go.GetComponentsInChildren<Animator>(true)) PreviewProps.SampleIdle(anim);
            placed++;
        }
        return placed;
    }

    /// <summary>One plant of <paramref name="s"/> at p, or false when water, a steep slope, a settlement's clear disc or the
    /// terrain edge rules it out. Public for the per-hex tile generators (HexSandboxTiles).</summary>
    public static bool TryPlant(Species s, Vector3 p, System.Random rng, GeoFrame frame, Func<Vector3, float> groundY, Func<Vector3, float> slopeDeg,
        IReadOnlyList<(Vector3 at, float radius)> avoid, Func<Vector3, bool> isWater, out Matrix4x4 matrix)
    {
        matrix = default;
        float yaw = (float)rng.NextDouble() * 360f, k = 0.75f + 0.5f * (float)rng.NextDouble();
        bool field = s.HeightM <= 0f;
        float w = field ? 900f + 800f * (float)rng.NextDouble() : s.WidthM * k, d = field ? 900f + 800f * (float)rng.NextDouble() : s.WidthM * k;
        float footprint = new Vector2(w, d).magnitude * 0.5f;
        if (p.x - footprint < 0f || p.z - footprint < 0f || p.x + footprint > frame.WidthM || p.z + footprint > frame.LengthM) return false;
        if (isWater(p) || slopeDeg(p) > MapLookScatter.MaxTreeSlopeDeg) return false;
        foreach (var (at, radius) in avoid)
            if (new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude < (radius + footprint) * (radius + footprint)) return false;
        var turn = Quaternion.Euler(0f, yaw, 0f);
        float y = groundY(p), lo = y, hi = y;
        foreach (var c in new[] { new Vector3(w, 0f, d), new Vector3(-w, 0f, d), new Vector3(w, 0f, -d), new Vector3(-w, 0f, -d) })
        {
            var corner = p + turn * (c * 0.5f);
            if (isWater(corner) || slopeDeg(corner) > MapLookScatter.MaxTreeSlopeDeg) return false;
            float cy = groundY(corner); lo = Mathf.Min(lo, cy); hi = Mathf.Max(hi, cy);
        }
        if (field)
        {
            if (hi - lo > MapLookScatter.MaxFieldRiseM) return false;
            matrix = Matrix4x4.TRS(new Vector3(p.x, lo - MapLookScatter.FieldDepthM, p.z), turn, new Vector3(w, hi - lo + MapLookScatter.FieldDepthM + 6f, d));
            return true;
        }
        // A model that keeps its aspect is scaled by width alone; a stand-in shape or a stretched tree takes HeightM.
        float yScale = s.KeepAspect && Resources.Load<GameObject>(ModelsFolder + "/" + s.Name) != null ? w : s.HeightM * k;
        matrix = Matrix4x4.TRS(new Vector3(p.x, lo - 0.03f * s.HeightM, p.z), turn, new Vector3(w, yScale, d));
        return true;
    }

    public static Vector3 Disc(System.Random rng, float radius)
    {
        double a = rng.NextDouble() * 2 * Math.PI, r = radius * Math.Sqrt(rng.NextDouble());
        return new Vector3((float)(r * Math.Cos(a)), 0f, (float)(r * Math.Sin(a)));
    }

    /// <summary>One renderer per material under <paramref name="parent"/>: a model's own materials, or the marker template
    /// tinted per colour for stand-ins. Every mesh and material made here is listed in <paramref name="owned"/>.</summary>
    public static void Emit(Transform parent, IEnumerable<Plant> plants, Material template, List<Object> owned)
    {
        var groups = new Dictionary<Material, (List<CombineInstance> items, bool shadows)>();
        var tints = new Dictionary<Color, Material>();
        var parts = new Dictionary<string, (Mesh mesh, int sub, Material mat, Matrix4x4 local)[][]>(StringComparer.Ordinal);
        Material Tinted(Color c)
        {
            if (tints.TryGetValue(c, out var m)) return m;
            m = new Material(template) { name = "sandbox-" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            owned.Add(m); return tints[c] = m;
        }
        // A species' variants: Resources/HexSandbox/<species>, <species>_2, <species>_3 ...; with none, its stand-in shape.
        (Mesh, int, Material, Matrix4x4)[][] VariantsOf(Species s)
        {
            if (parts.TryGetValue(s.Name, out var p)) return p;
            var models = new List<(Mesh, int, Material, Matrix4x4)[]>();
            for (int v = 1; s.HeightM > 0f; v++)
            {
                var prefab = Resources.Load<GameObject>(ModelsFolder + "/" + (v == 1 ? s.Name : $"{s.Name}_{v}"));
                if (prefab == null) break;
                models.Add(ModelParts(prefab, s.KeepAspect));
            }
            if (models.Count == 0)
                models.Add(MapLookShapes.Build(s.Shape).Select(part =>
                {
                    owned.Add(part.Mesh);
                    var colour = part.Colour != "bark" && s.Tint is Color t ? t : MapLookAssets.Palette[part.Colour];
                    return (part.Mesh, 0, Tinted(colour), Matrix4x4.identity);
                }).ToArray());
            return parts[s.Name] = models.ToArray();
        }
        foreach (var plant in plants)
        {
            var s = All[plant.Species];
            var variants = VariantsOf(s);
            var at = plant.Matrix.GetColumn(3);
            int pick = variants.Length == 1 ? 0 : (int)((uint)(Mathf.FloorToInt(at.x) * 73856093 ^ Mathf.FloorToInt(at.z) * 19349663) % (uint)variants.Length);
            foreach (var (mesh, sub, mat, local) in variants[pick])
            {
                if (!groups.TryGetValue(mat, out var g)) groups[mat] = g = (new List<CombineInstance>(), s.Shadows);
                g.items.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = parent.worldToLocalMatrix * plant.Matrix * local });
            }
        }
        foreach (var (mat, (items, shadows)) in groups)
        {
            var mesh = new Mesh { name = "sandbox-" + mat.name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(items.ToArray(), true, true); owned.Add(mesh);
            var go = new GameObject("sandbox-" + mat.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }

    /// <summary>A model's meshes in a unit frame: footprint centred, base at y = 0, its larger width 1 unit and (unless
    /// keepAspect) 1 unit tall, so the planting matrix's (width, height, width) scale gives the species' size.</summary>
    static (Mesh, int, Material, Matrix4x4)[] ModelParts(GameObject prefab, bool keepAspect)
    {
        var go = Object.Instantiate(prefab);
        try
        {
            var filters = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null).ToArray();
            var b = filters.Select(f => f.GetComponent<MeshRenderer>().bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            float width = Mathf.Max(b.size.x, b.size.z, 1e-4f), height = Mathf.Max(b.size.y, 1e-4f);
            // keepAspect: 1 unit = the model's width on every axis; TryPlant then scales y by the width too, so a rock scan
            // keeps its own height-to-width ratio.
            var norm = Matrix4x4.Scale(new Vector3(1f / width, keepAspect ? 1f / width : 1f / height, 1f / width)) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            return filters.SelectMany(f => f.GetComponent<MeshRenderer>().sharedMaterials.Select((m, i) => (f.sharedMesh, i, m, norm * f.transform.localToWorldMatrix)))
                .Where(p => p.i < p.sharedMesh.subMeshCount)
                .Where(p => { if (p.m == null) Debug.LogWarning($"HEX SANDBOX {prefab.name}: submesh {p.i} of {p.sharedMesh.name} has no material"); return p.m != null; })
                .ToArray();
        }
        finally { Object.DestroyImmediate(go); }
    }
}
